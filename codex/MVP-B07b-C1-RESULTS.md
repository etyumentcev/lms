# MVP-B07b-C1 — isolated component results

2026-10-11 · **IMPLEMENTED — READY FOR OWNER ACCEPTANCE (synthetic component only).** Основание: явное OWNER AUTHORIZATION — MVP-B07b-C1; [owner register](L03-OWNER-DECISION-REGISTER.md), [B07b-P contract](MVP-B07b-INTEGRATION-CONTRACT.md) принят с условиями локального isolated scope. B07a contract/implementation не менялись. Это не server integration и не G02 evidence acceptance.

## Архитектура и изменения

Trusted local test caller → bounded frozen ZIP snapshot → systemd user service/cgroup v2 → bubblewrap namespaces → ZIP adapter → существующий `compile_package` B07a → раздельный student/teacher staging → проверка обоих artifacts → private in-memory Result. После teardown и host cleanup возможен Ready; ошибки возвращают Invalid/Failed/Cancelled без receipt/artifacts. Worker не публикует файлы, не имеет DB/HTTP/production DI или permissions.

| Новый файл | Назначение |
|---|---|
| [model.py](../tools/content_import/model.py), [__init__.py](../tools/content_import/__init__.py) | Закрытые Limits/Artifact/BuildReceipt/Result; bytes и tuple assets, фиксированные integration diagnostics. |
| [zip_adapter.py](../tools/content_import/zip_adapter.py) | EOCD/central-directory preflight до ZipFile allocation; bounded entry count; paths/implicit-parent collisions/type/flags до записи; bounded raw-deflate decompression с фактическим общим byte count, CRC/size checks, nested archive rejection. Нет extract-all. |
| [sandbox.py](../tools/content_import/sandbox.py) | Фиксированный запуск без shell/argument expansion; cgroup memory/pids/CPU, RuntimeMaxSec, namespace mounts и clean environment; bounded stdout; termination evidence. Нет unisolated fallback. |
| [worker.py](../tools/content_import/worker.py) | Закреплённые dependencies, tmpfs source/student/teacher, вызов неизменённого B07a, запись/read-back обеих projections. |
| [verify.py](../tools/content_import/verify.py) | Canonical JSON, closed envelope/manifest/metadata, assets bytes/hash/path set, exact teacher.student, studentProjectionHash, teacherArtifactHash, packageHash против индекса распакованных файлов. Повторная проверка в parent. Не альтернативный compiler. |
| [importer.py](../tools/content_import/importer.py) | No-follow regular/nlink=1 local ZIP, size/inode/mtime/ctime rechecks, digest snapshot, closed Result, cleanup/quarantine. |
| [README](../tools/content_import/README.md) | Trusted caller usage, узкий ZIP profile, зависимости и ограничения. |
| [test_import.py](../tests/content_import/test_import.py) | 49 самостоятельных positive/negative tests и настоящие fault probes. |
| [evidence directory](evidence/MVP-B07b-C1/) | Логи component/B07a regression, measurements, platform, sample receipt, preservation check. |

Дополнен только owner register отдельной записью о разрешении; исторические решения/отчёты и B07b-P сохранены. Никаких C# / API / frontend / schema / migrations изменений.

## Invariants, writes и provenance

Actor — доверенный локальный synthetic test caller; org/course/user authority здесь не создаётся и не имитируется. Entities/VO — frozen ZIP snapshot, source index, две artifacts и BuildReceipt. Переход: snapshot → compile/verify → Ready либо Invalid/Failed/Cancelled. Invalid — отклонённый ZIP/content; Failed — runtime/resource/integrity/cleanup failure; Cancelled — отмена, в active-worker path только после проверки teardown. Любая private content error блокирует обе artifacts.

Component владеет только transient filesystem writes: уникальный 0700 root на вызов, input.zip 0400, read-only mount; source/student/teacher находятся в отдельном namespace tmpfs, каталоги 0700 и файлы 0400. B07a читает extracted source через собственный secure snapshot. Ready — возврат value после проверки bytes, завершения worker и cleanup, не durable commit. DB transaction/isolation/lock order/CAS/audit N/A: DB и shared durable state отсутствуют. Повтор — новый независимый attempt, без overwrite и duplicate publication. При потере caller/result нет обещания recovery; это не durable idempotency service.

Worker stdout — приватный structured envelope, stderr не показывается. Source contents, OS errors и stdout не попадают в diagnostics. Compiler diagnostics сохраняют fixed message/code и относительные координаты; integration codes отдельные. Quarantine после host cleanup failure: chmod 000 (0700 при ошибке chmod), Failed без artifacts; путь не возвращается. Удаление затем выполняет trusted test operator, без TTL/StoredAttachments. Same UID/root считаются доверенными, chmod не является auth boundary против них.

BuildReceipt имеет только фиксированные поля: receiptRevision, zipDigest, packageHash, studentProjectionHash, teacherArtifactHash, sourceRevision, compilerRevision, contractRevision, runtimeFingerprint и limits. Нет текущего времени/attempt ID, поэтому повтор одинаковых bytes/revision/runtime/profile воспроизводим. sourceRevision — метка trusted caller, не подтверждённый Git commit. Индекс исходных файлов сверяется с B07a packageHash; SHA-256 ZIP связывает transport bytes с этой сборкой. После snapshot подмена исходного ZIP не меняет результат. Во время capture замена/изменение отклоняется.

[Фактический результат](evidence/MVP-B07b-C1/sample-result.json) получен через isolated import N9 ZIP: Ready; packageHash `a1f45d3e931d128f7cf7725eb1c19b61e9d89aa368692c502b9b91a27be8958f`; studentProjectionHash `3caf281a564d8d33fc553bfd9cb74e8b8552685119c57820c509a460e9bf5c20`; teacherArtifactHash `179b71d75a00126680d0761afac9cd99a9814c2ba313944604d34d02e7afa19a`. Эти hashes совпадают с принятым B07a. ZIP для sample использует фиксированное время entries 2026-10-11 00:00:00 и сортировку paths.

Student Result содержит только student JSON и один student asset; teacher Result — отдельные bytes и два assets. Tests независимо ищут private canary, проверяют отсутствие packageHash/teacherArtifactHash в student JSON и неизменность всего student Artifact при private-only изменении. Полный Result/receipt/diagnostics приватен и не является student DTO. Семантическая тайна, явно помещённая автором в student материал, автоматически не распознаётся.

## Фактическая платформа и лимиты

[Platform evidence](evidence/MVP-B07b-C1/platform.json): Linux 7.0.0-38-generic x86_64, glibc 2.43, Python 3.14.4, bubblewrap 0.11.1, systemd 259.5; PyYAML 6.0.3, markdown-it-py 3.0.0, Pillow 12.1.1, Node 24.19.0, KaTeX 0.16.47. Worker проверяет версии Python packages; B07a проверяет KaTeX. Runtime fingerprint хеширует выбранные binaries, component code/README и KaTeX; это не attestation всего distro `/usr`. Trusted installation не должна меняться во время вызова.

| Ресурс | Experimental default / enforcement | Что доказано |
|---|---|---|
| ZIP / entries / expanded | 100,000,000 bytes / 2,000 / 100,000,000 bytes; preflight + фактическое stream counting | Exact и +1 границы на пониженных profiles, ZIP bomb и ложные sizes/entry counts deny. Это upper caps, не обещание успешной сборки любого 100 MB пакета. |
| Memory / swap | cgroup memory.max=536870912, memory.swap.max=0 | Effective files прочитаны; при 32 MiB probe systemd фиксирует oom-kill всего service. Host parent memory не ограничена этим cgroup; parent читает ограниченный envelope. |
| Tasks | pids.max=32, включая threads/reaper/Node | При tasks=8 fork отказал, pids.events.max>0, observed 8 tasks; descendants после завершения отсутствуют. |
| CPU | LimitCPU=20 s per process; CPUQuota=100%; aggregate cpu.stat polling ~20 ms | По 1 s tests: single CPU worker остановлен; три CPU children остановлены именно aggregate monitor. Возможен overshoot между sample/kill; **не hard exact aggregate cutoff**. |
| Wall time | RuntimeMaxSec=30 s, SIGKILL/control-group; parent deadline/cancel checks | Hung detached child profile 2 s и cancellation остановлены, measurements ниже. Это worker interval, не общий deadline filesystem/setup/control calls. |
| Output | Сумма JSON/assets ≤200,000,000 bytes; проверка до записи и read-back | Малый output profile → Failed, без partial success. Wire cap `2*output + entries*2048 + 1 MiB`, максимум один дополнительный 64 KiB read перед kill; flood test PASS. |
| Staging | tmpfs expanded+output+4 MiB, kernel page rounding, memory charged to cgroup | ENOSPC на пониженном profile; исходный ZIP на host занимает не более zip cap; namespaces уничтожены перед возвратом. |

В probe tree входят sandbox supervisor/init/worker и children. Ниже фактически наблюдённые значения последнего прогона; CPU/memory — последние доступные samples, не гарантированно финальная accounting величина. PID/start-time проверены после stop, cgroup отсутствует либо populated=0. Числа не являются worst-case upper bound.

| Probe | Reason / systemd result | Elapsed | CPU usec | Memory peak sample | Observed PIDs; teardown |
|---|---|---|---|---|---|
| `test_aggregate_cpu_budget_across_children` | I_CPU_LIMIT / signal | 0.9931 s | 1029876 | 5689344 | 6; empty/gone=True/True |
| `test_cancellation_kills_tree` | I_CANCELLED / signal | 0.3624 s | 24502 | 5443584 | 4; empty/gone=True/True |
| `test_cpu_limit` | I_WORKER_EXIT / exit-code | 1.0506 s | 997957 | 4845568 | 3; empty/gone=True/True |
| `test_hung_detached_child_timeout` | I_TIMEOUT / timeout | 2.0608 s | 24908 | 5398528 | 4; empty/gone=True/True |
| `test_memory_limit` | I_WORKER_EXIT / oom-kill | 0.2482 s | 52280 | 32907264 | 3; empty/gone=True/True |
| `test_namespace_no_network_secrets_db` | success / success | 0.1776 s | 26961 | 7786496 | 3; empty/gone=True/True |
| `test_normal_exit_kills_lingering_child` | success / success | 0.3144 s | 40718 | 5922816 | 4; empty/gone=True/True |
| `test_output_pipe_limit` | I_OUTPUT_LIMIT / signal | 0.0423 s | 14364 | 5033984 | 3; empty/gone=True/True |
| `test_process_limit` | success / success | 0.1708 s | 23498 | 7651328 | 8; empty/gone=True/True |
| `test_tmpfs_storage_limit` | success / success | 0.2094 s | 25925 | 10280960 | 3; empty/gone=True/True |

Полный [process evidence](evidence/MVP-B07b-C1/process-evidence.json) содержит unit IDs, PID lists, actual memory/swap/pids/cpu files, events и return codes. `setsid()` и ignore SIGTERM не спасают child: cgroup-wide SIGKILL и PID namespace teardown охватывают его. Проверены timeout, cancellation и нормальный выход main с оставшимся child. Systemd watchdog настроен независимо от parent; отдельный fault test смерти самого orchestrator NOT RUN. Отказ systemd/DBus или неубиваемый kernel D-state не доказан: без подтверждения teardown нет Ready/Cancelled, а watchdog остаётся ограниченным backstop, не доказательством G02.

## Positive/negative tests

```sh
B07_EVIDENCE_PATH=codex/evidence/MVP-B07b-C1/process-evidence.json \
  python3 -m unittest discover -s tests/content_import -v
```

**49 PASS, 0 failures/errors**; [log](evidence/MVP-B07b-C1/component-tests.log). 19 ZIP tests, 6 snapshot tests, 14 full-component tests, 10 supervisor tests. Checks включают full N9 ZIP; stored/deflate/data descriptor controls; bombs, oversized actual total, traversal/absolute/encoded paths, symlink/FIFO/socket/device attrs, ZIP Unix hardlink extra и реальный local hardlink, duplicates/case-fold/parent collisions, encryption, nested ZIP/SFX/TAR/signatures, unsupported compression, CRC/false header sizes, включая forged size с CRC усечённого содержимого; source race; hung/detached children; memory/aggregate CPU/tasks/output/tmpfs limits; in-flight/pre-cancel; cleanup failure; missing runtime; tampered assets/missing receipt digest; private compiler error; no partial success; receipt repeatability и student noninterference.

Isolation probe проверяет иной network namespace inode, недоступность host home и DB socket directory, отсутствие inherited secret environment, read-only input и отказ внешнего connection. Это доказательство протестированного namespace profile, не универсальный exploit/security audit. Hardlink в ZIP не имеет единого portable типа: Unix extras отвергаются целиком, extractor никогда не создаёт links; symlink/special mode entries отдельно denied.

B07a regression выполнена в этой задаче на неизменённом B07a:

```sh
SAPIENTUM_CONTENT_NODE=/home/tea/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node \
NODE_PATH=/home/tea/github/lms/src/Lms.Web/node_modules \
  python3 -m unittest discover -s tests/content_compiler -v
```

**74 PASS, 0 failures/errors**; [log](evidence/MVP-B07b-C1/b07a-regression.log). Application .NET regression **NOT RUN**: C# application не менялась; прежние 272 PASS не считаются новым запуском. HTTP/PostgreSQL/browser/E2E/physical security acceptance **NOT RUN / NOT CLAIMED**. При self-review устранена зависимость от ZipExtFile declared-size truncation: raw-deflate считает реальные bytes; отдельный атакующий тест подтверждает deny там, где стандартный reader возвращает усечённый success. Первый промежуточный прогон имел ошибку test measurement short write; измеритель исправлен до финального PASS, результаты не скрывают её как production defect.

## Ограничения и следующий owner gate

ZIP profile намеренно уже общего ZIP: нет ZIP64, extra fields (даже benign timestamps), comments/SFX на внешнем пакете или неизвестных flags. В README зафиксированы поддержанные nested archive signatures/formats; universal polyglot detection не заявляется. Требуются user systemd manager, cgroup v2 и namespaces/openat2; unsupported environment fail closed. ARM/macOS/Windows не проверены. `/usr`, kernel/filesystem, manager и caller UID доверены; host secrets в runtime installation недопустимы. Parent snapshot I/O, scheduler и control-plane latency не имеют hard completion proof. Runtime caps и passing tests **не закрывают G02**.

Для будущего B07b server integration нужны отдельные owner authorization и design: trusted import/teacher resource mapping без новых inferred grants; current processing/subject/source checks и accepted delayed revoke semantics; host/actor admission quotas; minimal durable storage/provenance/CAS/cleanup/pin protocol; bounded HTTP upload/download и безопасный renderer/cache; отдельная проверка supervisor/control-plane failure и hard I/O assumptions. Нет production TTL, public file service или готового browser preview. B08 отдельно владеет publisher capability, ContentVersionId, durable publication/open/assign; C1 этого не создаёт.

## Guardrails §7 и сохранность

- covered — actor/use case/states, fail-closed errors, package/projection integrity и независимые negative tests описаны выше.
- covered — S2/S3/S4/PRS1 и gates не менялись; никаких grants, narrowing restrictions или client authority.
- not applicable — org/course/group authorization, processing subjects, counts/pagination: нет server access paths или пользователей, только trusted synthetic caller; это обязательные будущие проверки, не PASS существующей авторизации.
- covered — file parents не имитируются, StoredAttachments не используются; immutable learning history не затронута.
- not applicable — DB transaction/locks/CAS/commit audit: только unique transient staging, возврат value после teardown/cleanup; retry/unknown outcome ограничения описаны.
- covered — validation/limits/diagnostics, component integration и negative probes; relevant B07a regression PASS; HTTP/browser/E2E NOT RUN.
- covered — нет real data, secrets в diagnostics или unsupported release claims; synthetic evidence only.
- covered — сохранены branch/index/HEAD/посторонние изменения; подробная baseline проверка добавлена ниже. Нет commit/merge/rebase/push/deploy.

Specialist skills model-domain/implement-use-case/implement-feature отсутствуют в доступном каталоге; их выполнение не заявляется. Канонический каталог `/home/tea/github/lms`, существующая dirty branch `codex/l1-s4-jwt-api-authorization` сохранена по AGENTS.md; переключения на main не было.

**B07b-C1 READY FOR OWNER ACCEPTANCE · B07b SERVER INTEGRATION NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**

Final documentation/preservation validation: PASS — 456 существующих файлов вне register неизменны по SHA-256; register append-only; HEAD/branch/index совпадают с baseline. Local links разрешаются, whitespace новых файлов и `git diff --check -- codex/L03-OWNER-DECISION-REGISTER.md` PASS. [Preservation evidence](evidence/MVP-B07b-C1/preservation.json).
