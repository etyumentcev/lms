# Offline content compiler

Run from the repository root using Python 3.11+ and the pinned dependencies in
`requirements.txt`. Tested on Linux x86_64. This is a local component, not a
production service, publication pipeline or runtime authorization boundary.

```sh
python3 -m tools.content_compiler tests/content_compiler/fixtures/counting \
  --package-limit 100000000 --output /private/build/new-output
python3 -m tools.content_compiler examples/course \
  --legacy-validate --package-limit 100000000
python3 -m unittest discover -s tests/content_compiler -v
```

The output directory must not exist, its parent must exist and be controlled by
the local caller. The containing directory is private (0700); student/ and
teacher/ are separate bundles. Only student/ is a candidate for future student
delivery. The parent also contains closed diagnostics and build metadata.
Neither bundle grants access or authorizes public hosting.

`compile_package(absolute_root, package_limit=...)` returns a closed build result.
On success, `studentArtifact` and `teacherArtifact` each contain immutable JSON
bytes and a mapping of artifact-relative asset paths to immutable bytes. On
invalid/failed results both artifacts and build metadata are absent. The caller
must keep the entire result private; only the selected bundle crosses a future
separately authorized delivery boundary. The compiler writes nothing; the CLI
stages both bundles and performs Linux renameat2(RENAME_NOREPLACE). It does not
overwrite output. Crash durability/fsync and publication transactions are not
promised. A retry encountering an existing destination must inspect it, not
assume rollback or overwrite it.

Input limits are decimal bytes. Package limit is required; document/image/static
defaults are 1,000,000 / 10,000,000 / 50,000,000. All input files, including unused
and private assets and inventory, contribute. Linux openat2 with RESOLVE_BENEATH
and RESOLVE_NO_SYMLINKS plus no-follow root traversal is required. Unsupported
platform/kernel fails closed. Identity, link count, byte limits and tree changes
are checked before/after reads and snapshot verification; compilation uses only
retained bytes. No network fetch, database, shell, Git lookup or production DI.
The filesystem/kernel and caller-controlled root are trusted infrastructure;
privileged hostile mounts or a malicious filesystem are not modeled.

PDF and SVG are rejected. PNG/JPEG/WebP are decoded and verified with Pillow;
TXT is strict UTF-8. Decompression-bomb warnings/errors reject images. Semantic
secrets in student-classified bytes cannot be inferred automatically.

Legacy validation is separate and emits no artifacts. No executable legacy
validator existed in the repository: compatibility means preserving the written
v1 semantics and existing fixtures, not equivalence to an unavailable historical
binary. Unknown fields warn without semantics, course-wide duplicate problem IDs
warn, internal symlinks and local Markdown links remain allowed. Its filesystem
profile is deliberately not the revision 1.1 secure snapshot profile.

JCS serialization supports exactly the output contract's ASCII keys, Unicode
strings, safe integers, arrays, booleans and null; floats and non-contract keys
are rejected. It is not a general-purpose RFC 8785 implementation. Compiler
revision is pinned in code; changing Markdown rewriting requires a new revision.

Math uses dedicated inline and display dollar AST nodes and KaTeX 0.16.47.
Install Node.js and the pinned adjacent package.json dependency with your approved
package installation process. Node must be on PATH or SAPIENTUM_CONTENT_NODE must
name its absolute executable path. NODE_PATH may locate a trusted preinstalled
KaTeX; its exact version is verified before source reads. Missing runtime fails
closed. A fixed Node script receives JSON formulas on stdin (shell=False); no
source is evaluated as JavaScript, no resource is fetched and no HTML is returned.

Literal dollars must be escaped; code remains literal. Formula text is preserved.
KaTeX uses strict errors, fresh macros, bounded expansion and denied trust; unsafe
URL/HTML/image commands are rejected. This validates the safe KaTeX subset, not
arbitrary TeX or mathematical truth. Parser timeout is 30 seconds; failure is
atomic. Compiler revision is sapientum-python-2. Rendering, sanitization, server
authorization, HTTP and publication remain outside this component.
