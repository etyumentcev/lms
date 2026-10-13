"""Closed build result, stable diagnostics and the integer-only JCS domain."""
import hashlib
import json
from dataclasses import dataclass

REVISION = '1.1'
COMPILER = 'sapientum-python-2'
MAX_INTEGER = 9007199254740991


def canonical(value):
    # All object keys in the output contract are ASCII; values have no floats.
    # This is RFC 8785 for this restricted domain (not a general JCS library).
    def check(v):
        if isinstance(v, dict):
            if any(not isinstance(k, str) or not k.isascii() for k in v):
                raise ValueError('Non-contract key')
            for item in v.values(): check(item)
        elif isinstance(v, list):
            for item in v: check(item)
        elif isinstance(v, str):
            v.encode('utf-8', errors='strict')
        elif type(v) is int:
            if abs(v) > MAX_INTEGER: raise ValueError('Unsafe integer')
        elif v is not None and type(v) is not bool:
            raise ValueError('Non-contract value')
    check(value)
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')


def digest(data):
    return hashlib.sha256(data).hexdigest()


@dataclass(frozen=True)
class Location:
    file: str = 'content-package.yaml'
    line: int = 1
    column: int = 1

    def json(self): return dict(file=self.file, line=self.line, column=self.column)


class Reject(Exception):
    def __init__(self, code, location=Location(), related=(), failed=False):
        self.code, self.location, self.related, self.failed = code, location, related, failed


# No input values, file contents or OS exception strings enter messages.
MESSAGES = {
    'E_MATH': 'Invalid or unsafe mathematical expression.',
    'E_CONFIG': 'Invalid compiler configuration.', 'E_IO': 'Input could not be read.',
    'E_INTERNAL': 'Compiler failed.', 'E_INPUT_CHANGED': 'Input changed during snapshot.',
    'E_FILE_LINK': 'Linked filesystem entry is forbidden.', 'E_FILE_TYPE': 'Non-regular input is forbidden.',
    'E_FILE_MISSING': 'Declared file is missing.', 'E_LIMIT': 'Input exceeds configured byte limit.',
    'E_PATH': 'Invalid or escaping local path.', 'E_PATH_COLLISION': 'Paths collide under case folding.',
    'E_YAML': 'Invalid or ambiguous YAML.', 'E_FIELD_REQUIRED': 'Required field is missing.',
    'E_FIELD_TYPE': 'Field has an invalid type.', 'E_FIELD_VALUE': 'Field has an invalid value.',
    'E_FIELD_UNKNOWN': 'Unknown field.', 'W_FIELD_UNKNOWN': 'Unknown legacy field has no semantics.',
    'E_SCHEMA_MIX': 'Mixed content revisions.', 'E_SCHEMA_UNSUPPORTED': 'Unsupported schema.',
    'E_REVISION_REQUIRED': 'Compilation requires explicit revision 1.1.',
    'E_ID_FORMAT': 'Invalid identifier.', 'E_ID_DUPLICATE': 'Duplicate identifier.',
    'W_ID_DUPLICATE': 'Legacy problem identifier is repeated across lessons.',
    'E_ORDER_DUPLICATE': 'Duplicate order in target scope.', 'E_TARGET_KIND': 'Invalid target kind.',
    'E_TARGET_MISSING': 'Target does not exist in this package.',
    'E_MATERIAL_DUPLICATE': 'Only one material of this kind is allowed per problem.',
    'E_PACKAGE_STRUCTURE': 'Invalid course structure.', 'E_INVENTORY': 'Inventory does not match package.',
    'E_ASSET_DUPLICATE': 'Asset path is declared twice.', 'E_ASSET_SCOPE': 'Asset is outside content ownership scope.',
    'E_ASSET_VISIBILITY': 'Asset visibility conflicts with its path or referring document.',
    'E_ASSET_TYPE': 'Unsupported or malformed asset bytes or media type.',
    'E_ASSET_UNDECLARED': 'Local destination is not a declared asset.',
    'W_ASSET_UNUSED': 'Declared asset is unused.', 'E_LINK_TARGET': 'Local document or directory link is forbidden.',
    'E_URI': 'Unsafe external URI.', 'E_EXTERNAL_IMAGE': 'External image is forbidden.',
    'E_LINK_TYPE': 'Image destination is not an image.', 'E_MARKDOWN': 'Forbidden Markdown construct.',
    'W_VIDEO_PROVIDER': 'Unsupported video provider; retained as an ordinary link.',
}


class Diagnostics:
    def __init__(self): self.items = []

    def add(self, code, loc=Location(), related=()):
        self.items.append(dict(severity='WARNING' if code.startswith('W_') else 'ERROR', code=code,
                               **loc.json(), message=MESSAGES[code], related=[x.json() for x in related]))

    def ordered(self):
        return sorted(self.items, key=lambda x: (x['file'].encode(), x['line'], x['column'], x['severity'], x['code'], x['message']))
