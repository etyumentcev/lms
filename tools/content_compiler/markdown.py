"""CommonMark AST validation with parser-derived destination source edits.

Only parsed link destinations are replaced; source text, labels, math and code
remain verbatim. Each parser instance owns its helpers (no global mutation).
"""
from types import SimpleNamespace
import re
from urllib.parse import urlsplit
from markdown_it import MarkdownIt
from markdown_it.rules_inline import link, image, autolink
from markdown_it.rules_block import reference
from .model import Location, Reject
from .snapshot import SEGMENT

PROVIDERS = {'rutube.ru', 'vkvideo.ru', 'youtube.com', 'www.youtube.com', 'youtu.be'}


def relative_destination(file, destination):
    if not destination or destination.startswith(('/', '\\')) or re.match(r'^[A-Za-z]:', destination):
        raise Reject('E_PATH')
    if any(c in destination for c in '\\%?#') or any(ord(c) < 32 or ord(c) == 127 for c in destination):
        raise Reject('E_PATH')
    parts = file.split('/')[:-1]
    for part in destination.split('/'):
        if part == '.': continue
        if part == '..':
            if not parts: raise Reject('E_PATH')
            parts.pop()
        elif not SEGMENT.fullmatch(part): raise Reject('E_PATH')
        else: parts.append(part)
    return '/'.join(parts)


def process(body, file, offset, resolve, diagnostics, expressions=None):
    md = MarkdownIt('commonmark', {'html': True, 'inline_definitions': True, 'store_labels': True}).enable('table')
    from .math_validation import install_math
    install_math(md)
    if expressions is None: expressions = []
    md.helpers = SimpleNamespace(**vars(md.helpers))
    md.validateLink = lambda _: True  # Reject dangerous schemes explicitly, not silently as text.
    md.normalizeLink = lambda value: value
    env, tokens, edits = {}, [], {}
    line_starts = [0]
    for i, ch in enumerate(body):
        if ch == '\n': line_starts.append(i+1)
    current = []
    block_cursors = {}
    original_destination = md.helpers.parseLinkDestination

    def mapping(text, first, last, sequential=False):
        # Block parser removes indentation/quote/list prefixes. Match each source
        # line in its AST block range, retaining exact Unicode character indexes.
        result, cursor = [], block_cursors.get((first,last),line_starts[first]) if sequential else line_starts[first]
        end = line_starts[last] if last < len(line_starts) else len(body)
        for chunk in text.splitlines(keepends=True):
            value = chunk.rstrip('\n')
            found = body.find(value, cursor, end) if value else cursor
            if found < 0:
                # The table rule unescapes pipes before creating inline tokens.
                chars, indexes = [], []
                i = cursor
                while i < end:
                    if body[i:i+2] == '\\|': i += 1
                    chars.append(body[i]); indexes.append(i); i += 1
                found = ''.join(chars).find(value)
                if found < 0: raise Reject('E_INTERNAL', failed=True)
                mapped = indexes[found:found+len(value)]
                result.extend(mapped)
                cursor = mapped[-1]+1 if mapped else cursor
            else:
                result.extend(range(found, found + len(value)))
                cursor = found + len(value)
            if chunk.endswith('\n'):
                newline = body.find('\n', cursor, end)
                if newline < 0: raise Reject('E_INTERNAL', failed=True)
                result.append(newline)
                cursor = newline + 1
        if sequential: block_cursors[(first,last)] = cursor
        return result

    def location(index):
        import bisect
        line = bisect.bisect_right(line_starts, index)-1
        return Location(file, line+offset+1, index-line_starts[line]+1)

    def check(raw, index, is_image, used):
        loc = location(index)
        # Inspect the original spelling as well as decoded destination. Encoded
        # local bytes and backslashes never get normalized into accepted paths.
        try:
            if raw.startswith('//'): raise Reject('E_URI')
            scheme = re.match(r'^([A-Za-z][A-Za-z0-9+.-]*):', raw)
            if scheme and not re.match(r'^[A-Za-z]:', raw):
                u = urlsplit(raw)
                if u.scheme != 'https' or not raw.startswith('https://') or not u.hostname or u.username is not None or u.password is not None or any(ord(c) <= 32 for c in raw) or '\\' in raw:
                    raise Reject('E_URI')
                try: u.port
                except ValueError: raise Reject('E_URI') from None
                if is_image: raise Reject('E_EXTERNAL_IMAGE')
                return None
            path = relative_destination(file, raw)
            return resolve(path, is_image, used, loc)
        except Reject as e:
            diagnostics.add(e.code, loc, e.related)
        except ValueError:
            diagnostics.add('E_URI', loc)
        return None

    def capture(text, pos, maximum):
        value = original_destination(text, pos, maximum)
        if value.ok and current: current[-1]['captures'].append((text, pos, value.pos))
        if current and any(ord(c) < 32 and c not in '\t\n\r' for c in text[pos:].split(')')[0].split('\n')[0]):
            frame = current[-1]
            if 'state' in frame:
                source_map = frame['state'].env['_source_map']
            else:
                source_map = mapping(text, frame['line'], min(len(line_starts), frame['line'] + text.count('\n') + 2))
            diagnostics.add('E_PATH', location(source_map[pos]))
        return value
    md.helpers.parseLinkDestination = capture

    def wrap_inline(rule, is_image=False, auto=False):
        def wrapped(state, silent):
            start = state.pos
            frame = {'captures': [], 'state': state, 'start': start, 'image': is_image}
            current.append(frame)
            old_len = len(state.tokens)
            try: success = rule(state, silent)
            finally: current.pop()
            if success and not silent:
                source_map = state.env['_source_map']
                if auto:
                    raw = state.src[start+1:state.pos-1]
                    if '@' in raw and ':' not in raw: raw = 'mailto:' + raw
                    check(raw, source_map[start+1], False, True)
                elif frame['captures']:
                    text, a, b = frame['captures'][0]
                    angle = text[a:a+1] == '<'
                    lo, hi = a+int(angle), b-int(angle)
                    raw = text[lo:hi]
                    out = check(raw, source_map[lo] if lo < len(source_map) else source_map[a], is_image, True)
                    if out: edits[(source_map[a], source_map[b-1]+1)] = ('<' + out + '>') if angle else out
                else:
                    # AST carries the exact selected reference; all definitions
                    # are validated independently by the block rule below.
                    for token in state.tokens[old_len:]:
                        if token.type in ('image', 'link_open'):
                            label = token.meta.get('label')
                            ref = state.env.get('references', {}).get(label)
                            if ref:
                                check(ref['href'], ref['_destination_index'], token.type == 'image', True)
                            break
            return success
        return wrapped

    def wrapped_reference(state, start, end, silent):
        frame = {'captures': [], 'line': start}
        current.append(frame)
        old_tokens = len(state.tokens)
        try: success = reference(state, start, end, silent)
        finally: current.pop()
        if success and not silent and frame['captures']:
            text, a, b = frame['captures'][0]
            source_map = mapping(text, start, min(len(line_starts), start + text.count('\n') + 2))
            angle = text[a:a+1] == '<'
            lo, hi = a+int(angle), b-int(angle)
            index = source_map[lo]
            out = check(text[lo:hi], index, False, False)
            if out: edits[(source_map[a], source_map[b-1]+1)] = ('<' + out + '>') if angle else out
            for token in state.tokens[old_tokens:]:
                if token.type == 'definition':
                    ref = state.env['references'][token.meta['id']]
                    if ref['map'][0] == start: ref['_destination_index'] = index
        return success

    original_inline_parse = md.inline.parse
    def inline_parse(text, parser, environment, out):
        old_map = environment.get('_source_map')
        if current and current[-1].get('image'):
            frame = current[-1]
            label_start = frame['start'] + 2
            environment['_source_map'] = old_map[label_start:label_start+len(text)]
        try: return original_inline_parse(text, parser, environment, out)
        finally: environment['_source_map'] = old_map
    md.inline.parse = inline_parse

    md.inline.ruler.at('link', wrap_inline(link))
    md.inline.ruler.at('image', wrap_inline(image, True))
    md.inline.ruler.at('autolink', wrap_inline(autolink, auto=True))
    md.block.ruler.at('reference', wrapped_reference, {'alt': []})
    md.block.parse(body, md, env, tokens)
    for token in tokens:
        if token.type == 'math_block':
            loc = location(token.meta['position'])
            if not token.meta['closed']: diagnostics.add('E_MATH', loc)
            else: expressions.append((token.content, True, loc))
        if token.type == 'html_block': diagnostics.add('E_MARKDOWN', location(line_starts[token.map[0]]))
        if token.type != 'inline': continue
        env['_source_map'] = mapping(token.content, *token.map, sequential=True)
        children = []
        md.inline.parse(token.content, md, env, children)
        def inspect(nodes):
            for child in nodes:
                if child.type == 'math':
                    loc = location(child.meta['position'])
                    if not child.meta['closed']: diagnostics.add('E_MATH', loc)
                    else: expressions.append((child.content, child.meta['display'], loc))
                if child.type == 'html_inline': diagnostics.add('E_MARKDOWN', location(env['_source_map'][0]))
                if child.type == 'text' and re.search(r'!include\b|\{\{\s*include\b|\{%\s*include\b', child.content):
                    diagnostics.add('E_MARKDOWN', location(env['_source_map'][0]))
                if child.children: inspect(child.children)
        inspect(children)
        value = token.content.strip()
        if value.startswith('https://') and '\n' not in value and ' ' not in value:
            check(value, env['_source_map'][0], False, True)
            try:
                if urlsplit(value).hostname not in PROVIDERS: diagnostics.add('W_VIDEO_PROVIDER', location(env['_source_map'][0]))
            except ValueError: pass
    for (a, b), value in sorted(edits.items(), reverse=True): body = body[:a] + value + body[b:]
    return body
