# B07b-C1 isolated synthetic ZIP import

Python library for a trusted local synthetic test caller. No CLI that accepts
remote requests, HTTP, PostgreSQL, DI registration, grants, publication, durable
storage or production retention. B07a is called unchanged.

```python
from pathlib import Path
from tools.content_import.importer import import_zip
from tools.content_import.sandbox import Runtime

result = import_zip(
    Path('/trusted/synthetic/course.zip'),
    runtime=Runtime(Path('/trusted/runtime/node'), Path('/trusted/modules/katex')),
    source_revision='synthetic-source-1',
)
# Entire result is PRIVATE. Only result.student is the student projection.
# Ready requires both artifacts, hash checks, worker teardown and local cleanup.
```

`Result` is Ready, Invalid, Failed or Cancelled. Non-Ready results have no receipt
or artifacts. Artifacts contain immutable JSON bytes and `(path, bytes)` tuples,
not host paths or public URLs. Teacher includes private materials; never forward
Result/receipt/diagnostics as a student response. Cancellation accepts a
`threading.Event`; only confirmed sandbox termination returns Cancelled from an
in-flight run. Repeated calls create independent attempts, not a durable
idempotency protocol. BuildReceipt intentionally excludes wall time, temporary
IDs and operational evidence, so identical ZIP/revision/profile/runtime produce
identical receipts. ZIP metadata changes can change zipDigest without changing
packageHash. Source revision is a caller label, not verified Git provenance.

## Platform and fixed dependencies

Tested Linux x86_64, Python 3.14.4, PyYAML 6.0.3, markdown-it-py 3.0.0,
Pillow 12.1.1, Node 24.19.0, KaTeX 0.16.47; existing B07a needs openat2.
Requires `/usr/bin/bwrap`, `/usr/bin/systemd-run`, `/usr/bin/systemctl`, a working
user systemd manager, unprivileged user namespaces and cgroup v2 memory/pids/cpu
controllers. No unisolated fallback. Runtime paths are trusted installation
configuration; never sourced from the ZIP. Namespace exposes read-only `/usr`,
only the two component code directories, Node/KaTeX, and a frozen ZIP snapshot;
no host home, DB sockets, credentials or host network. The distro `/usr` runtime,
kernel, systemd manager and caller UID are trusted. Fingerprint covers selected
binaries, component files and KaTeX, not supply-chain attestation of every
system library. Do not mutate the trusted installation during a call.

Source ZIP is opened no-follow/nonblocking, regular/nlink=1, bounded into a
0700 private directory, with inode/size/mtime/ctime rechecks. Later replacement
of the original does not alter retained bytes. A hostile same-UID actor or
privileged filesystem/kernel is outside scope. Source files are created only
inside sandbox tmpfs, mode 0400, and passed to B07a's secure snapshot reader;
the original ZIP is a read-only mount. Source and student/teacher staging are
separate private directories. Extracted-source mode is not an independent
sandbox against the trusted worker changing its own files.

## Narrow ZIP profile

Single-disk ZIP, stored or deflated, optional data descriptors. No ZIP64, archive
comments/prefix/trailing data, entry comments/extra records, encryption, unknown
flags, symlinks/devices/FIFOs/sockets or Unix link metadata. All extra fields are
rejected, including benign timestamp extensions: create a plain ZIP accordingly.
EOCD and central-directory count are bounded before ZipFile object allocation.
Paths and implicit parents are checked before writes; duplicates/case-fold
collisions, traversal/absolute/drive/UNC, NUL, control characters, percent/colon,
query/fragment syntax and depth >32 are denied. Each expanded raw-deflate stream is counted independently of the declared size;
ZipExtFile truncation is not used. CRC/size inconsistencies fail. No extract-all or destination from archive content.
Nested ZIP (including a prefixed ZIP), TAR and common archive signatures/suffixes
are rejected; this is not a universal polyglot or semantic content detector.
B07a remains responsible for inventory, schemas, asset types and projections.

## Experimental limits, not operational acceptance

`Limits` defaults: ZIP 100,000,000 bytes; expanded 100,000,000 bytes; 2,000 entries;
combined artifact content/assets 200,000,000 bytes; cgroup memory 512 MiB,
swap disabled, 32 tasks (threads included), 20 CPU seconds and 30 wall seconds.
Tests may lower these values only. Memory/pids are cgroup limits, CPUQuota=100%
is a rate limit; aggregate cpu.stat is sampled every ~20 ms and killed at the
budget, with per-process LimitCPU as backstop. Sampling/kill scheduling can
overshoot: CPU budget is not an exact hard aggregate cutoff. RuntimeMaxSec is
an independent systemd watchdog if the Python supervisor disappears. KillMode
control-group/SIGKILL terminates detached descendants; namespace teardown removes
all tmpfs staging. OS D-state/host failure cannot be bounded by these tests.

Tmpfs capacity is expanded+output+4 MiB, rounded to kernel pages and charged to
the same memory cgroup. Logical artifact output is checked before writes and on
read-back; parent bounds the wire envelope at 2*output+entries*2048+1 MiB, with at
most one 64 KiB read overshoot before kill. Worker stdout is private structured
JSON; stderr is discarded, not surfaced as diagnostics. Host snapshot disk usage
is bounded by ZIP limit. No hard end-to-end filesystem I/O or control-plane
completion guarantee is claimed. Limits apply per invocation; actor/host-wide
admission, quotas and authority do not exist in C1.

Failed/cancelled tmpfs disappears with the worker namespace. Host snapshot is
removed before Ready. If deletion fails, Result becomes Failed with no artifacts
and root is chmod 000 (0700 fallback); trusted test operator must remove it.
Quarantine is private to this UID, not protection from that same trusted UID or
root, and has no production TTL. There is no retention of successful disk builds;
the caller owns returned in-memory bytes.

## Verification

```sh
B07_EVIDENCE_PATH=/tmp/b07c1-evidence.json \
  python3 -m unittest discover -s tests/content_import -v
```

Tests use the installed trusted Node path and existing repository KaTeX package.
Supervisor `_probe` is a private fault-injection seam accepting a trusted test
file; it is never wired to `import_zip` or ZIP metadata. Tests verify namespace
isolation, actual cgroup files, OOM/tasks/output/CPU limits, detached children,
cancellation and no partial success. OS metrics and observed PID/start-time
identities are evidence only, not part of the stable BuildReceipt.

See `codex/MVP-B07b-C1-RESULTS.md` for exact commands/evidence/limitations.
G02 remains blocked; server integration, HTTP, browser and B08 are unauthorized.
