# MVP-STORAGE-PROFILE-01 — bounded physical qualification

2026-10-12. **NO-GO in the currently authorized environment. STORAGE-SEAL-01 ISOLATED COMPONENT ACCEPTED; STORAGE-SEAL-PHYSICAL-01 OPEN.** Latest owner decision in this task supersedes the previous report's acceptance-pending headline, without rewriting that checkpoint. No supported private fs-verity filesystem is available to the unprivileged test process. No implementation or normative changes were made.

## Contract and scope

Trusted coordinator → qualify one private Linux x64 ext4 profile → retained writer exclusion, immutable exact version, durable reservation and conservative recovery. Entities: operation/generation, original, kernel measurement and PostgreSQL receipt. Existing transitions remain `PhysicalPending → Sealed`, or `PhysicalPending → OutcomeUnknown → Quarantined`; no Unknown promotion/replay/release. Physical proof does not authorize a user read. Actor/org selectors are trusted internal inputs; physical facts come from the kernel and receipts from authoritative PostgreSQL. HTTP photo-read, production DI, legacy attachments, domain schema, NR1 and host configuration remain unchanged.

Read AGENTS, guardrails/reconciliation, NR1 N3/N5/N6, ADR-L15/L18 and STORAGE-SEAL results. The model-domain/implement-use-case/implement-feature skills remain absent from the supplied catalog; no execution of them is claimed. Existing ledger owns ReadCommitted transactions, control-row then original-row locks, epoch/CAS confirmation, synchronous commit and no automatic retry. No new transaction, audit or domain effect is introduced.

## Environment and cause

[Captured environment](evidence/MVP-STORAGE-PROFILE-01/environment.txt), [standalone probe](evidence/MVP-STORAGE-PROFILE-01/probe.py), [probe output](evidence/MVP-STORAGE-PROFILE-01/probe.json).

| Parameter | Observed value |
|---|---|
| Kernel | Linux x86_64 7.0.0-38-generic, #38-Ubuntu SMP PREEMPT_DYNAMIC, Sep 4 2026 |
| Caller | tea, UID/GID 1000; effective/permitted capabilities zero; no sudo/root used |
| Kernel config | CONFIG_EXT4_FS=y, CONFIG_FS_VERITY=y, CONFIG_FS_VERITY_BUILTIN_SIGNATURES=y |
| Driver capability | `/sys/fs/ext4/features/verity` = supported |
| ext4 backing mount | `/dev/nvme0n1p2` at `/`; `/var/tmp` is on this same primary filesystem |
| Mount | rw,relatime,stripe=128; detailed proc options include data=ordered, barrier, commit=5 |
| Filesystem block size | 4096 bytes |
| Probe file | synthetic regular file, owned by UID 1000, 0600; private temporary root 0700; writable FD closed before O_RDONLY ioctl |
| ext4 inode flags | 0x80000 (extents), no verity flag |
| Signature policy | `/proc/sys/fs/verity/require_signatures` = 0 |
| Userspace tools | mke2fs/e2fsprogs 1.47.2; fsverity utility and qemu-system-x86_64 not found in PATH |
| Device access | block device and loop-control root:disk 0660; caller is not in disk group |

Both `FS_IOC_ENABLE_VERITY` and `FS_IOC_MEASURE_VERITY` return **95 (EOPNOTSUPP; Python names the alias ENOTSUP)** on the fresh extent-based ext4 file. Kernel support is established independently. The measurement failure, which does not depend on enable's hash/block parameters or writer access, identifies an unavailable superblock verity feature under the documented ext4 contract. The raw superblock was not read because the block device is inaccessible to UID 1000: feature-bit absence is a contract-based diagnosis, not a claimed raw superblock dump. It is not a missing kernel build option, non-extent probe inode, read-only mount, retained writer, or required-signature failure.

On `/tmp` (tmpfs) both calls return **25 / ENOTTY**. This is a control for an unsupported filesystem, not an alternative qualification candidate. The mount inventory exposes only one ext4 filesystem; bind mounts and containers backed by that filesystem cannot add its missing on-disk feature. No alternative-filesystem search was pursued.

[Linux fs-verity documentation](https://docs.kernel.org/filesystems/fsverity.html#filesystem-support) specifies ext4's on-disk `verity` feature and extent-based files. [ioctl contract](https://docs.kernel.org/filesystems/fsverity.html#fs-ioc-enable-verity) distinguishes EOPNOTSUPP from writer exclusion ETXTBSY. These sources support the diagnosis; actual host outcomes are in the probe evidence.

Prerequisites for the single proposed profile: Linux x64 with CONFIG_EXT4_FS/CONFIG_FS_VERITY, ext4 formatted with `-O verity,extent`, 4096-byte filesystem and verity blocks, writable ordered-journal mount with barriers, private durable 0700 root owned by the non-root coordinator, regular writable-by-owner inode opened O_RDONLY for enable, no writable descriptors or mappings, signatures not mandated for this unsigned component. No special `verity` mount option substitutes for the superblock feature. e2fsprogs must understand verity. Existing main filesystem must not be tuned.

## Physical evidence and limits

| Owner obligation | Result in this run |
|---|---|
| 1. Real successful enable | BLOCKED: errno 95 on ext4; no physical positive |
| 2. Retained writable FD | NOT RUN on supported filesystem; injected refusal tests are model evidence only |
| 3. Retained writable mmap | NOT RUN; errno 95 is not evidence of writer exclusion |
| 4. Post-seal write/truncate denial | NOT RUN: no sealed physical original |
| 5. Measurement/exact version after reopen | NOT RUN on sealed physical file; model hash/length/version checks pass |
| 6. New process receipt/inspection | Real sealed-original process restart NOT RUN; existing restart tests reconstruct coordinator/ledger with ModelKernel |
| 7. Missing/corrupt/mismatched originals | Model missing/hash/length/version/quarantine cases pass; physical verity corruption verification NOT RUN |
| 8. Worker crash/reservation/Unknown | Existing actual Python worker SIGKILL/reap test passes: private bytes and reservation retained, Recover marks Unknown, new admission denied. Kernel used after recovery is ModelKernel; this is not sealed-original durability evidence |

Process crash, filesystem remount, and power-loss are separate obligations. The actual killed writer test covers process death before confirmation, and model confirmation/ACK-loss tests cover ledger semantics. **No remount, VM reset, host reboot, power cut, device-cache/flush validation or coordinated DB/files restore drill was performed.** fsync and a process restart do not prove power-loss durability. Retained reservation limits and safe-denial tests cannot establish filesystem capacity overhead or physical seal.

## Exact execution

All commands run from `/home/tea/github/lms` as UID 1000. Tool sandbox failed before execution with `mountinfo path is not absolute`; approved sandbox escalation retained the same non-root UID and did not grant kernel capabilities. No automatic approval rejection occurred.

```sh
uname -a
id
rg 'CONFIG_(FS_VERITY|EXT4_FS)' /boot/config-7.0.0-38-generic
cat /sys/fs/ext4/features/verity
cat /proc/sys/fs/verity/require_signatures
findmnt -T /var/tmp -o TARGET,SOURCE,FSTYPE,OPTIONS
findmnt -rn -t ext4,tmpfs -o TARGET,SOURCE,FSTYPE,OPTIONS
cat /proc/fs/ext4/nvme0n1p2/options
ls -l /dev/nvme0n1p2 /dev/loop-control /dev/kvm
command -v mkfs.ext4 fsverity unshare qemu-system-x86_64
/usr/sbin/mkfs.ext4 -V
python3 /tmp/storage-profile-01/probe.py
/tmp/b07c2g/sdk/dotnet test tests/Lms.Infrastructure.Tests/Lms.Infrastructure.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~StorageSealTests --logger trx --results-directory /tmp/storage-profile-01/targeted
/tmp/b07c2g/sdk/dotnet test Lms.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/storage-profile-01/regression
```

The probe script is retained verbatim and may be run from its evidence path. It creates only synthetic temporary files, closes writers, attempts measurement and enable, records errno and removes its own temporary directory. Targeted tests use existing disposable PostgreSQL 17.6 fixtures. Logs/TRX are linked below. No new tests were added to manufacture a green physical result.

## Bounded setup plan — NOT EXECUTED, separate permission required

Only candidate: a **2 GiB disposable ext4 image**, 4 KiB blocks, verity + extents, on a dedicated runner or as a temporary file in `/var/tmp`; never a production block device. Administrator provisions the filesystem; the qualification process remains UID 1000 with no capabilities, no raw-device/image mutation access during qualification. No administrator bypass counts as writer exclusion evidence. The image is not mounted or formatted in this task.

Concrete provisioning sequence proposed for approval (not a verified setup):

```sh
# Run initial creation as tea; retain printed unique path for review.
profile_dir=$(mktemp -d /var/tmp/lms-storage-profile-01.XXXXXX)
chmod 700 "$profile_dir"
truncate -s 2G "$profile_dir/ext4.img"
mkdir "$profile_dir/mnt"
mkfs.ext4 -F -b 4096 -O verity,extent -E lazy_itable_init=0,lazy_journal_init=0 "$profile_dir/ext4.img"
# Administrator-only provisioning, requiring separate explicit permission:
sudo chown root:root "$profile_dir/ext4.img"
sudo chmod 600 "$profile_dir/ext4.img"
sudo mount -t ext4 -o loop,rw,nodev,nosuid,noexec,data=ordered "$profile_dir/ext4.img" "$profile_dir/mnt"
sudo install -d -o 1000 -g 1000 -m 0700 "$profile_dir/mnt/originals"
# Read-only verification; tests subsequently execute as UID 1000.
findmnt -T "$profile_dir/mnt/originals" -o TARGET,SOURCE,FSTYPE,OPTIONS
sudo dumpe2fs -h "$profile_dir/ext4.img"
# After tests close all descriptors and processes, administrator unmounts:
sudo umount "$profile_dir/mnt"
```

Before executing, verify the uniquely created image is a regular file, not a symlink/device, and the mount destination is an empty directory; record allocated loop identity and refuse any unexpected source. Final cleanup must target only the recorded image/directory after verifying unmount and loop detachment. No broad device detach or recursive privileged cleanup. Root provisioning is outside the unprivileged component threat model and requires owner approval explicitly under task §1. No root invocation is proposed for enable/write/mmap/inspection assertions. No fstab/sysctl/kernel/production-partition changes.

CI requires a dedicated Linux runner with authorized loop/mount provisioning, sufficient disk space, this kernel configuration, recorded kernel/e2fsprogs versions and an unprivileged test account. No existing `.github` CI directory is present. A generic container runner does not prove a usable filesystem. Runner availability and the proposed provisioning sequence are NOT VERIFIED. Qualification must add a strict profile test entry point that fails if enable is unsupported (the existing conditional safe-denial test is not a CI qualification gate), then implement all eight physical assertions including a separate coordinator process. Remount needs a separately controlled phase; power-loss requires its own fault model and experiment, not inference from remount success. This plan is bounded to ext4; no fallback engines or unlimited search.

## Conditions for protected photo-read

Physical acceptance remains blocked until the supported profile proves real enable, retained FD/mmap exclusion, post-seal mutations, reopened and restarted receipt/exact-version checks, physical missing/damage/mismatch behavior and conservative worker recovery. Durability claims must state their tested boundary and accepted storage-stack assumptions; restore/rollback hazards remain explicit. Separate owner authorization must then cover receipt-to-original/generation/parent mapping, independent S2/S3/current-processing authorization for every read/Range/preview, verification on the actual held delivery descriptor, CLOCK-DEPLOY-01/deadline gates and bounded transport. No legacy auto-promotion, public URL, HTTP route or production DI follows from this report.

## Guardrails §7 review

| Checklist | Disposition |
|---|---|
| Actor/use case/invariants/states | covered above; no normative change |
| S2/S3/PRS1 and blocked gates | covered: no runtime/access change, exact OFF |
| Org/course/group/ownership | N/A: no new user access path; existing org admission tests rerun |
| Affected subjects/embedded sources | N/A: synthetic local probes only, no new processing permission |
| Lists/counts/aggregates | N/A: no exposed query or pagination |
| Attachments/history | covered: originals synthetic; legacy and checkpoints untouched; physical proof blocked |
| Transactions/locks/CAS/retry/unknown | covered: existing isolated ledger semantics rerun, no edits |
| Validation/limits/safe errors | covered: unsupported ioctl denies; no fallback; bounded candidate |
| Unit/integration/negative/E2E | tests below; browser/frontend N/A because no UI/API edit; physical gaps explicit |
| Schema/API/security/unrelated changes | covered by preservation verification; report/evidence only |
| Real data/secrets/readiness | synthetic only, no secrets or readiness promotion |
| Exact test results/gaps | below; models and physical positives distinguished |

**STORAGE-SEAL-01 ACCEPTED · STORAGE-PROFILE-01 NO-GO (CURRENT ENVIRONMENT) · STORAGE-SEAL-PHYSICAL-01 OPEN · PHOTO READ BLOCKED · CLOCK-DEPLOY-01 OPEN · G02 BLOCKED · H04/READY BLOCKED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.** Stop for owner acceptance; provisioning requires a separate explicit decision.

## Preservation and changed paths

Only this report and `codex/evidence/MVP-STORAGE-PROFILE-01/` were created. No production or test implementation changed. [Preservation evidence](evidence/MVP-STORAGE-PROFILE-01/preservation.json): 772/772 baseline files unchanged (tracked and non-ignored untracked files, excluding live `.obsidian/` state and ignored build outputs). Branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` and index SHA-256 unchanged. Dirty checkout was not switched to main under AGENTS' preservation exception. No commit, push, merge, rebase or deploy. New-report local links and whitespace validated.

## Final test results

- Targeted: **25 passed / 0 failed / 0 skipped**. [Log](evidence/MVP-STORAGE-PROFILE-01/targeted.log), [TRX](evidence/MVP-STORAGE-PROFILE-01/targeted.trx). Both physical-profile tests explicitly report BLOCKED (ext4 errno 95, tmpfs errno 25); their PASS is safe denial only.
- Backend regression: **1012 passed / 0 failed / 2 skipped** — Domain 43, Application 340, Infrastructure 283, API 346 passed. [Log](evidence/MVP-STORAGE-PROFILE-01/regression.log), [per-assembly counters, TRX filenames and skip names](evidence/MVP-STORAGE-PROFILE-01/test-summary.json). Targeted tests are included in regression, not additive.
- Skips: existing opt-in Browser_work and Browser_read tests without LMS_BROWSER_READ=1. Browser E2E, frontend, physical positive seal, remount and power-loss qualification are NOT RUN.
- Report links/whitespace PASS; final baseline verification 772/772 unchanged, branch/HEAD/index unchanged. Owner acceptance pending for this NO-GO report. No privileged setup was performed.
