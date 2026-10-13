"""Strict YAML nodes retain original Unicode scalar/key coordinates."""
import unicodedata
import yaml
from yaml.nodes import MappingNode, ScalarNode, SequenceNode
from .model import Location, Reject


class Mapping(dict):
    def __init__(self, loc):
        super().__init__()
        self.loc, self.values_at, self.keys_at = loc, {}, {}

    def at(self, key): return self.values_at.get(key, self.loc)


class Sequence(list):
    def __init__(self, values, locations):
        super().__init__(values)
        self.locations = locations


def parse(text, file, offset=0, strict=True):
    def loc(mark): return Location(file, mark.line + offset + 1, mark.column + 1)
    try:
        for token in yaml.scan(text):
            if strict and isinstance(token, (yaml.tokens.AliasToken, yaml.tokens.AnchorToken, yaml.tokens.TagToken)):
                raise Reject('E_YAML', loc(token.start_mark))
        node = yaml.compose(text, Loader=yaml.SafeLoader)
        def convert(n):
            if isinstance(n, MappingNode):
                result = Mapping(loc(n.start_mark))
                for key, value in n.value:
                    if not isinstance(key, ScalarNode) or key.tag != 'tag:yaml.org,2002:str' or key.value == '<<':
                        raise Reject('E_YAML', loc(key.start_mark))
                    k = key.value
                    if k in result: raise Reject('E_YAML', loc(key.start_mark), [result.keys_at[k]])
                    result.keys_at[k], result.values_at[k] = loc(key.start_mark), loc(value.start_mark)
                    result[k] = convert(value)
                return result
            if isinstance(n, SequenceNode): return Sequence([convert(x) for x in n.value], [loc(x.start_mark) for x in n.value])
            if not isinstance(n, ScalarNode): raise Reject('E_YAML')
            if n.tag == 'tag:yaml.org,2002:str':
                if strict and unicodedata.normalize('NFC', n.value) != n.value: raise Reject('E_FIELD_VALUE', loc(n.start_mark))
                n.value.encode('utf-8')
                return n.value
            if n.tag == 'tag:yaml.org,2002:int':
                # YAML integer syntax is allowed, quoted numbers remain strings.
                return yaml.safe_load(n.value)
            if n.tag == 'tag:yaml.org,2002:null': return None
            if n.tag == 'tag:yaml.org,2002:bool': return yaml.safe_load(n.value)
            if n.tag == 'tag:yaml.org,2002:float': return float('nan')
            raise Reject('E_YAML', loc(n.start_mark))
        result = convert(node)
        if not isinstance(result, Mapping): raise Reject('E_YAML', Location(file, offset+1))
        return result
    except yaml.YAMLError as e:
        raise Reject('E_YAML', loc(e.problem_mark) if getattr(e, 'problem_mark', None) else Location(file, offset+1)) from None
    except (UnicodeError, RecursionError, ValueError):
        raise Reject('E_YAML', Location(file, offset+1)) from None


def document(data, file, strict=True):
    try:
        text = data.decode('utf-8', errors='strict').replace('\r\n', '\n').replace('\r', '\n')
    except UnicodeError: raise Reject('E_YAML', Location(file)) from None
    lines = text.splitlines(keepends=True)
    if not lines or lines[0] != '---\n': raise Reject('E_YAML', Location(file))
    end = next((i for i in range(1, len(lines)) if lines[i].rstrip('\n') == '---'), None)
    if end is None: raise Reject('E_YAML', Location(file))
    meta = parse(''.join(lines[1:end]), file, 1, strict)
    body = ''.join(lines[end+1:])
    if strict and unicodedata.normalize('NFC', body) != body:
        raise Reject('E_FIELD_VALUE', Location(file, end+2))
    return meta, body, end+1
