"""Dollar math AST and isolated, fixed-version KaTeX syntax checking."""
import importlib
import json
import os
from pathlib import Path
import shutil
import subprocess
from .model import Reject


def install_math(md):
    terminators = set(importlib.import_module('markdown_it.rules_inline.text')._TerminatorChars) | {'$'}

    def text(state, silent):
        end = state.pos
        while end < state.posMax and state.src[end] not in terminators:
            end += 1
        if end == state.pos: return False
        if not silent: state.pending += state.src[state.pos:end]
        state.pos = end
        return True

    def inline(state, silent):
        start = state.pos
        if state.src[start] != '$': return False
        width = 2 if state.src[start:start+2] == '$$' else 1
        end = start + width
        while end < state.posMax:
            if state.src[end] == '\\': end += 2; continue
            if state.src[end:end+width] == '$' * width: break
            end += 1
        closed = end < state.posMax
        if not silent:
            token = state.push('math', 'math', 0)
            token.content = state.src[start+width:end]
            token.meta = {'position': state.env['_source_map'][start], 'display': width == 2, 'closed': closed}
        state.pos = min(end + width, state.posMax)
        return True

    def block(state, start, end, silent):
        if state.is_code_block(start): return False
        pos = state.bMarks[start] + state.tShift[start]
        first = state.src[pos:state.eMarks[start]]
        if not first.startswith('$$'): return False
        if silent: return True
        line = start
        content = first[2:]
        parts = []
        closed = False
        while True:
            closing = content.find('$$')
            if closing >= 0:
                # Anything after the delimiter remains ordinary Markdown; only
                # consume a block when its closing delimiter ends the line.
                if content[closing+2:].strip(): return False
                parts.append(content[:closing]); closed = True; break
            parts.append(content)
            line += 1
            if line >= end: break
            content = state.src[state.bMarks[line]+state.tShift[line]:state.eMarks[line]]
        token = state.push('math_block', 'math', 0)
        token.content = '\n'.join(parts)
        token.map = [start, min(line+1, end)]
        token.meta = {'display': True, 'closed': closed, 'position': pos}
        state.line = min(line+1, end)
        return True

    md.inline.ruler.at('text', text)
    md.inline.ruler.before('escape', 'math', inline)
    md.block.ruler.before('fence', 'math_block', block, {'alt': ['paragraph', 'reference', 'blockquote', 'list']})


def validate(expressions, diagnostics, configuration_only=False):
    if not expressions and not configuration_only: return
    node = os.environ.get('SAPIENTUM_CONTENT_NODE') or shutil.which('node')
    if not node or not os.path.isabs(node) or not os.access(node, os.X_OK):
        raise Reject('E_CONFIG', failed=True)
    payload = [{'source': item[0], 'display': item[1]} for item in expressions]
    # Arguments, module and executable are trusted installation configuration;
    # untrusted formulas go only through stdin, never shell or command arguments.
    environment = {'PATH': os.path.dirname(node)}
    if 'NODE_PATH' in os.environ: environment['NODE_PATH'] = os.environ['NODE_PATH']
    try:
        completed = subprocess.run([node, str(Path(__file__).with_name('math-check.cjs'))],
                                   input=json.dumps(payload).encode(), stdout=subprocess.PIPE,
                                   stderr=subprocess.DEVNULL, env=environment, timeout=30,
                                   check=False, shell=False)
        if completed.returncode == 2: raise Reject('E_CONFIG', failed=True)
        if completed.returncode != 0: raise Reject('E_INTERNAL', failed=True)
        answers = json.loads(completed.stdout)
        if not isinstance(answers, list) or len(answers) != len(expressions) or any(type(x) is not bool for x in answers):
            raise Reject('E_INTERNAL', failed=True)
        for valid, (_, _, location) in zip(answers, expressions):
            if not valid: diagnostics.add('E_MATH', location)
    except (OSError, subprocess.TimeoutExpired, ValueError):
        raise Reject('E_INTERNAL', failed=True) from None
