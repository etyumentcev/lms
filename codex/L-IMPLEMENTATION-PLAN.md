# L — Sequential implementation plan

**PROPOSAL. L1–L4 не начаты и этим документом не разрешаются.** L0 завершает анализ; следующее действие — согласовать [результат](L0-RESULTS.md) и [decision register](L-DECISION-REGISTER.md). Accepted D1/D2/D3: VPS/сервер, Compose, ASP.NET Core Identity. Production readiness остаётся NOT_READY.

## Общая стратегия проверки и веток

Канонический checkout `/home/tea/github/lms`, последовательно, без новых worktrees/агентов. L0 создан от exact `c7a500f`; другие ветки не изменять. **Будущий** порядок после отдельного разрешения: review/accept L0 → интегрировать K+L0 в согласованную main → L1 → review/merge → L2 → review/merge → L3 → review/merge → L4. Сейчас никаких merge/push.

Каждый этап отдельная будущая ветка-кандидат `codex/l1-identity`, `codex/l2-resource-protection`, `codex/l3-deployment-backup`, `codex/l4-pilot-acceptance` от accepted integrated predecessor. Перед стартом проверить clean status/base, owner gates и actual HEAD: текущий main не предполагается равным K. Если integration ещё не разрешена, остановиться на результате этапа; не собирать параллельный стек веток. При изменениях main rebase только своей ещё не опубликованной ветки с разрешённым workflow, затем повторить affected checks; чужую историю не переписывать. Каждый этап reviewable independently **от принятого predecessor**, не независим от dependencies.

Каждый PR/commit этапа содержит своё решение B, контракт/migration notes, tests/evidence и report. Сохранять C–K reports. Исторический baseline K: 366 frontend/260 backend; не требовать неизменного количества tests после расширений, требовать отсутствие необъяснённых failures/skips. Реальные PostgreSQL/HTTP/browser проверки там, где fake не доказывает boundary. Команды baseline в [K matrix](K-TEST-MATRIX.md). Не запускать destructive release runner на real data.

## L1 — Identity

**Цель:** настоящий account/session lifecycle вместо pilot demo bearer, с сохранением Actor и scope boundaries. Сложность **высокая**, ориентир 5–9 инженерных дней после gates для manual-recovery варианта; не обещание сроков, SMTP/MFA/new user requirements могут расширить объём.

Prerequisites: accepted L0/K, разрешение L1, C01/C02 закрыты. Только synthetic accounts до C08/C09; VPS/domain не нужны для local isolated tests. B01–B03 инженер принимает после проверки.

Изменения:

1. Infrastructure Identity EF dependency в версии, совместимой с pinned .NET/EF; additive context/mappings/migrations по [ADR-L01](../docs/adr/ADR-L01-identity-integration.md), legacy ID inventory, no fake backfill/cascade history.
2. Restricted provisioning CLI и challenge/reset workflow по C01/C02/[ADR-L02](../docs/adr/ADR-L02-account-provisioning.md). Atomic user→actor→access provisioning, dry-run, audit без секретов; публичной registration/Teacher role selection нет.
3. API Identity cookie/session/stamp/active validation, CSRF, login/session/logout/password routes с C4-compatible errors. Minimal login abuse protections входят в L1, чтобы authentication slice не принимался без них; общая ресурсная система — L2. Dev bearer отдельный explicit Development profile, pilot rejects demo.
4. Web session provider/login/password screens из existing primitives; fetch и XHR CSRF, startup/focus/cross-tab lifecycle, state/Blob cleanup/remount, no command replay. App landing/deep return отражает доступный account/roster, не demo role selector как login.
5. Test fixtures через реальные UserManager/SignInManager/HTTP cookies в isolated DB; сохранение existing tests и separate dev-mode JWT regression. Локальный TLS fixture/DP persistence для session tests; настоящая инфраструктура L3.

Tests: clean/upgrade migrations, unique actor mapping, partial/concurrent provisioning, challenge replay/expiry/password policy, login enumeration/lockout, malformed/mixed claims, authenticated Student/Teacher cross-scope negatives, file revoke, CSRF all unsafe methods, logout cookie replay/reset/deactivation, restart keys, DB failure fail-closed, account switch/expiry/no stale drafts/no retries. Real browser Submit→Revise→Accept с Identity на synthetic dataset, builds/full regressions после relevant targeted checks.

Exit: real Identity E2E passes; old history/IDs/annotations untouched; owner C01/C02 reflected; production demo unreachable; report explicitly NOT_READY (L2–L4 pending). Risks: identity↔actor confusion, duplicate role source, stale principal, login/logout CSRF, account-switch leak, single-use token races. Не закрывать privacy/device/hosting gates этим результатом.

## L2 — Resource protection

**Цель:** bounded resource admission и безопасное обращение с pending/orphans. Сложность **высокая**, ориентир 4–8 дней после accepted L1 и workload/retention inputs.

Prerequisites: accepted L1, разрешение L2; C06 объём/квоты и C08 pending retention до финального product config/destructive cleanup. B07 technical enforcement можно проверить на fixture thresholds; незакрытые C06/C08 не разрешают выбрать реальные квоты или удалять real files.

Изменения: API rate/concurrency/timeout/body bounds, upload early admission; durable budgets/reservations migration и disk reconciliation; quota startup validation; pending expiry/row-lock claim/tombstone coordination, idempotent cleanup lease/retry/quarantine; errors/Retry-After и frontend понятные failures без retries команды. Existing MaxBytes/annotation constraints сохраняются. Подготовить interface maintenance barrier для backup L3. Точки: AttachmentsController, AttachmentService, IAccessPolicy/transactional claim boundary, LocalFileAttachmentStorage, Program; все новые schemas только technical resources. [ADR-L06](../docs/adr/ADR-L06-resource-protection.md).

Tests: simultaneous users/global quota, simultaneous same actor, real PostgreSQL reservation races, multipart no-length/oversize/cancel, worst-case image validation memory, DB commit uncertainty, cleanup vs submit/message/delete, failed unlink/DB outage, orphan lease, restart reconciliation, isolated ENOSPC. Scope/immutable originals regressions; rate limiter partition cardinality, school NAT legitimate burst. Не превращать rate limit в max attempts Domain rule.

Exit: ledger+physical storage reconcile, no linked history deletion, finite resource queues, explicit capacity-dependent config/proposal values; K-S02 app portion/K-S05 evidence recorded, edge/host proof ещё L3/L4. Owner-непринятые quotas оставляют acceptance incomplete. Risks: double accounting/freeing, active writer misclassified orphan, lost commit, arbitrary quotas мешают уроку.

## L3 — Deployment & backup

**Цель:** воспроизводимое защищённое развёртывание и доказанное восстановление согласованного набора. Сложность **высокая**, ориентир 4–8 инженерных дней плюс ожидание domain/server/access и длительность drill.

Prerequisites: accepted L1/L2, разрешение L3; C03 host/domain/OS, C04 recovery/window, C05 storage/key custody, C09 operators. C06 capacity inputs; C08 до загрузки любых реальных работ. B04–B06/B09 технически завершить на выбранной среде.

Изменения: отдельный pilot Compose/reverse proxy, production images без demo, private network, explicit secrets/DP keys, trusted forwarded headers/HTTPS/security headers/CSRF deployment, health/restart/edge bounds/log rotation/alerts. Single migrate job, maintenance/drain/lock, pinned release manifest, rollback matrix. Backup/dump+files+keys/manifest/checksums/authenticated encryption/off-host transfer/retention; restore и incident/access/key runbooks. [ADR-L03](../docs/adr/ADR-L03-vps-deployment.md), [L04](../docs/adr/ADR-L04-https-secrets.md), [L05](../docs/adr/ADR-L05-backup-restore.md), [L08](../docs/adr/ADR-L08-operations-privacy.md).

Tests: local production-config rehearsal, compose validation/image inspection, network ports/headers spoof, cookie Secure/CSRF via HTTPS, renewal/rotation, restart retention, migrate failure/rollback. Clean-host restore из off-host (не исходных volumes), all original checksums/history/Identity read and synthetic write flow; revoke restored sessions и access reconciliation. Backup corruption/missing secret/failed transfer/low disk alerts получает реальный operator. RTO измеряет весь incident recovery, не только DB import.

Exit: signed-off operator evidence, C04 targets выполнены измерениями или gate возвращён владельцу, complete restore/rollback/recovery docs; domain/TLS/public boundary проверены на supplied host. Local-only implementation не называется deployed pilot. Risks: DNS/IPv6 exposure, unknown host performance, key loss, inconsistent snapshots, resurrected access после restore, large copy window. Никакого real-data launch до L4.

## L4 — Pilot acceptance

**Цель:** определить READY_FOR_PILOT либо NOT_READY по фактам выбранной среды/группы. Сложность **средняя/высокая**, ориентир 2–5 дней для выбранного доступного device scope, не включая устранение обнаруженных дефектов/ожидание hardware.

Prerequisites: accepted L1–L3, разрешение L4; C01–C09 закрыты для real pilot, назначены operator/testers и workload/device budgets. Если C07 roster/material workaround неприемлем либо C08 требует exceptional erasure implementation, сначала отдельный согласованный scope с проверками; L4 не прячет этот gap.

Изменения: reuse K Playwright sequence с настоящими Identity accounts/CSRF/cookies и отдельными Student A/B, scoped/foreign Teacher; безопасный acceptance harness без production reset/dev-token; reports/evidence/runbooks/readiness matrix. Только доказанные scoped defect fixes отдельными reviewable commits, не расширение Domain.

Tests: все шесть экранов, 2 ordered real camera photos, immutable hashes, annotations обеих страниц, revision/new attempt/accept/history/reload; wrong roles/scopes/expiry/logout/reset/deactivation/cross-tab; CSRF/limits/restart. Повторить restore с оператором из L3 procedure и сверить RPO/RTO. Performance mix+concurrency/soak на candidate host с real-size photos/long history/large Lesson, peak RAM/CPU/disk/WAL/backup overlap, измерить p95/p99 и frontend latency по C06. Физические устройства и реальные OS/browser/input по [ADR-L07](../docs/adr/ADR-L07-device-browser-scope.md)/K protocol; missing hardware остаётся BLOCKED.

Exit: каждый release gate имеет evidence, operator/date/commit/environment; no unresolved P0/P1, выбранные quotas/recovery/privacy/device budgets выполнены. Owner явно принимает scope и старт real pilot. READY_FOR_PILOT не означает READY_FOR_PRODUCTION; failure любого обязательного gate → NOT_READY с next actions. Risks: small sample ≠ soak, Chrome emulation ≠ Safari/Pencil, green health ≠ restore, синтетические PNG ≠ реальные фото.

## Что не блокирует локальную подготовку и что блокирует release

В L0 разрешены только документы/проверки/commit. После отдельного разрешения L1 C01/C02 достаточно для синтетической разработки; C03/C05 не нужны для этих tests. В L2 можно проектировать/test механизмы с fixture values до получения host; production thresholds зависят C06/C03. L3 local config/restore rehearsal возможны заранее, но без owner infrastructure inputs нельзя закрыть deployment. L4 никогда не закрывает C08/C09 или physical evidence за владельца. Сроки выше — оценка инженерной сложности, не принятое обязательство.
