"""Closed component values. Receipt and diagnostics are private author data."""
from dataclasses import dataclass, asdict


@dataclass(frozen=True)
class Limits:
    zip_bytes: int = 100_000_000
    expanded_bytes: int = 100_000_000
    entries: int = 2000
    output_bytes: int = 200_000_000
    memory_bytes: int = 536_870_912
    cpu_seconds: int = 20
    wall_seconds: int = 30
    tasks: int = 32

    def validate(self):
        ceilings = Limits()
        for k, v in asdict(self).items():
            if type(v) is not int or not 0 < v <= getattr(ceilings, k):
                raise ValueError('Invalid synthetic profile')
        if self.memory_bytes < 16_777_216 or self.tasks < 4:
            raise ValueError('Invalid synthetic profile')


@dataclass(frozen=True)
class Artifact:
    content: bytes
    assets: tuple[tuple[str, bytes], ...]


@dataclass(frozen=True)
class BuildReceipt:
    receiptRevision: str
    zipDigest: str
    packageHash: str
    studentProjectionHash: str
    teacherArtifactHash: str
    sourceRevision: str
    compilerRevision: str
    contractRevision: str
    runtimeFingerprint: str
    limits: tuple[tuple[str, int], ...]


@dataclass(frozen=True)
class Result:
    status: str
    diagnostics: tuple
    receipt: BuildReceipt | None = None
    student: Artifact | None = None
    teacher: Artifact | None = None
    evidence: dict | None = None


class Denied(Exception):
    def __init__(self, code, status='Invalid'):
        self.code, self.status = code, status


def diagnostic(code):
    # No archive names, OS messages, stdout or source bytes in integration errors.
    return {'severity': 'ERROR', 'code': code, 'message': 'Local import did not complete.'}
