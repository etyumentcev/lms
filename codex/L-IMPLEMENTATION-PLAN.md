# L — Sequential implementation plan

**PROPOSAL. L1–L4 не начаты и этим документом не разрешаются.** L0.1 расширяет анализ; следующее действие — согласовать [результат](L01-RESULTS.md), [PD gates](L01-DECISION-GATES.md) и [decision register](L-DECISION-REGISTER.md). Accepted D1–D5: VPS/сервер, Compose, ASP.NET Core Identity, российское ООО оператор, compliance до первого real-data пилота. Production readiness остаётся NOT_READY.

## Общая стратегия проверки и веток

Канонический checkout `/home/tea/github/lms`, последовательно, без новых worktrees/агентов. L0 создан от exact `c7a500f`; другие ветки не изменять. **Будущий** порядок после отдельного разрешения: review/accept L0 + L0.1 → интегрировать K+L0 в согласованную main → L1 → review/merge → L2 → review/merge → L3 → review/merge → L4. Сейчас никаких merge/push.

Каждый этап отдельная будущая ветка-кандидат `codex/l1-identity`, `codex/l2-resource-protection`, `codex/l3-deployment-backup`, `codex/l4-pilot-acceptance` от accepted integrated predecessor. Перед стартом проверить clean status/base, owner gates и actual HEAD: текущий main не предполагается равным K. Если integration ещё не разрешена, остановиться на результате этапа; не собирать параллельный стек веток. При изменениях main rebase только своей ещё не опубликованной ветки с разрешённым workflow, затем повторить affected checks; чужую историю не переписывать. Каждый этап reviewable independently **от принятого predecessor**, не независим от dependencies.

Каждый PR/commit этапа содержит своё решение B, контракт/migration notes, tests/evidence и report. Сохранять C–K reports. Исторический baseline K: 366 frontend/260 backend; не требовать неизменного количества tests после расширений, требовать отсутствие необъяснённых failures/skips. Реальные PostgreSQL/HTTP/browser проверки там, где fake не доказывает boundary. Команды baseline в [K matrix](K-TEST-MATRIX.md). Не запускать destructive release runner на real data.

## L1 — Identity & Personal Data Lifecycle

**Цель:** настоящий account/session lifecycle вместо pilot demo bearer, с сохранением Actor и scope boundaries. Сложность **высокая**, ориентир 5–9 инженерных дней после gates для manual-recovery варианта; не обещание сроков, SMTP/MFA/new user requirements могут расширить объём.

Prerequisites: accepted K/L0/L0.1, отдельное разрешение L1; C01/C02 и минимальный пакет PD-G02/03/04/08, принципы PD-G07/роль PD-G10 по L01-DECISION-GATES согласованы. Правовые основания и ADR согласуются до новых workflows. Только synthetic accounts; VPS/domain и полное live evidence не нужны для isolated tests. B01–B03 инженер принимает после проверки.

Изменения:

1. Infrastructure Identity EF dependency в версии, совместимой с pinned .NET/EF; additive context/mappings/migrations по [ADR-L01](../docs/adr/ADR-L01-identity-integration.md), legacy ID inventory, no fake backfill/cascade history.
2. Restricted provisioning CLI и challenge/reset workflow по C01/C02/[ADR-L02](../docs/adr/ADR-L02-account-provisioning.md). Atomic user→actor→access provisioning, dry-run, audit без секретов; публичной registration/Teacher role selection нет.
3. API Identity cookie/session/stamp/active validation, CSRF, login/session/logout/password routes с C4-compatible errors. Minimal login abuse protections входят в L1, чтобы authentication slice не принимался без них; общая ресурсная система — L2. Dev bearer отдельный explicit Development profile, pilot rejects demo.
4. Web session provider/login/password screens из existing primitives; fetch и XHR CSRF, startup/focus/cross-tab lifecycle, state/Blob cleanup/remount, no command replay. App landing/deep return отражает доступный account/roster, не demo role selector как login.
5. Минимальный purpose/basis/version/representative evidence, case/deadline register, privacy block на read/write/files/queue, отзыв/уточнение/защищённая выдача по ADR-L09. Не строить parent portal/admin UI без необходимости; manual operator path допустим лишь с доказанной полнотой. Поля/тексты по PD-G02/03/04, без автоматического паспорта/email/DOB.
6. Test fixtures через реальные UserManager/SignInManager/HTTP cookies в isolated DB; сохранение existing tests и separate dev-mode JWT regression. Локальный TLS fixture/DP persistence для session tests; настоящая инфраструктура L3.

Tests: clean/upgrade migrations, unique actor mapping, partial/concurrent provisioning, challenge replay/expiry/password policy, login enumeration/lockout, malformed/mixed claims, authenticated Student/Teacher cross-scope negatives, file revoke, CSRF all unsafe methods, logout cookie replay/reset/deactivation, restart keys, DB failure fail-closed, account switch/expiry/no stale drafts/no retries. Real browser Submit→Revise→Accept с Identity на synthetic dataset, builds/full regressions после relevant targeted checks.

Дополнительные tests: активация без основания запрещена; version/withdrawal/representative, purpose expiry, request deadlines/export без чужих ПД, blocked work недоступен whole-course Teacher; signed-out субъект может подать обращение через утверждённый канал.

Exit: Identity + минимальный privacy lifecycle E2E passes; old history/IDs/annotations untouched; owner C01/C02 reflected; production demo unreachable; report explicitly NOT_READY (L2–L4 pending). Risks: identity↔actor confusion, duplicate role source, stale principal, login/logout CSRF, account-switch leak, single-use token races. Не закрывать privacy/device/hosting gates этим результатом.

## L2 — Resource Protection & Data Lifecycle

**Цель:** bounded resource admission и безопасное обращение с pending/orphans. Сложность **высокая**, ориентир 4–8 дней после accepted L1 и workload/retention inputs.

Prerequisites: accepted L1, разрешение L2; C06 объём/квоты и C08 pending retention до финального product config/destructive cleanup. PD-G07/08 должны определить retention/destruction/holds и принять ADR-L10 до разрушительных механизмов. B07 technical enforcement можно проверить на fixture thresholds; незакрытые C06/C08 не разрешают выбрать реальные квоты или удалять real files.

Изменения: API rate/concurrency/timeout/body bounds, upload early admission; durable budgets/reservations migration и disk reconciliation; quota startup validation; pending expiry/row-lock claim/tombstone coordination, idempotent cleanup lease/retry/quarantine; errors/Retry-After и frontend понятные failures без retries команды. Existing MaxBytes/annotation constraints сохраняются. Подготовить interface maintenance barrier для backup L3. Точки: AttachmentsController, AttachmentService, IAccessPolicy/transactional claim boundary, LocalFileAttachmentStorage, Program; все новые schemas только technical resources. [ADR-L06](../docs/adr/ADR-L06-resource-protection.md).

Дополнительно: реализовать принятый отдельный privileged erasure workflow ADR-L10, retention по категории/цели, manifest и audit evidence; обычный orphan cleanup никогда не удаляет linked history. Работа с файлами/metadata/DomainEvents/annotations/смешанными субъектами — отдельные проверяемые шаги, app role без bypass.

Tests: erasure manifest scope, immutable обычных commands, absence по всем stores, partial failure/retry, mixed-subject preservation, сроки/акт/log export; simultaneous users/global quota, simultaneous same actor, real PostgreSQL reservation races, multipart no-length/oversize/cancel, worst-case image validation memory, DB commit uncertainty, cleanup vs submit/message/delete, failed unlink/DB outage, orphan lease, restart reconciliation, isolated ENOSPC. Scope/immutable originals regressions; rate limiter partition cardinality, school NAT legitimate burst. Не превращать rate limit в max attempts Domain rule.

Exit: ledger+physical storage reconcile; linked history не удаляется обычным cleanup, только approved exception workflow с доказательствами; finite resource queues, explicit capacity-dependent config/proposal values; K-S02 app portion/K-S05 evidence recorded, edge/host proof ещё L3/L4. Owner-непринятые quotas оставляют acceptance incomplete. Risks: double accounting/freeing, active writer misclassified orphan, lost commit, arbitrary quotas мешают уроку.

## L3 — Secure Deployment & Recovery

**Цель:** воспроизводимое защищённое развёртывание и доказанное восстановление согласованного набора. Сложность **высокая**, ориентир 4–8 инженерных дней плюс ожидание domain/server/access и длительность drill.

Prerequisites: accepted L1/L2, разрешение L3; C03 host/domain/OS, C04 recovery/window, C05 storage/key custody, C09 operators. C06 capacity inputs; PD-G05/06/07/09/10/12/13 для выбранной среды, правовые/организационные prerequisites до реальных ПД. C08 раскрыт PD gates. B04–B06/B09 технически завершить на выбранной среде.

Изменения: отдельный pilot Compose/reverse proxy, production images без demo, private network, explicit secrets/DP keys, trusted forwarded headers/HTTPS/security headers/CSRF deployment, health/restart/edge bounds/log rotation/alerts. Single migrate job, maintenance/drain/lock, pinned release manifest, rollback matrix. Backup/dump+files+keys/manifest/checksums/authenticated encryption/off-host transfer/retention; restore и incident/access/key runbooks. [ADR-L03](../docs/adr/ADR-L03-vps-deployment.md), [L04](../docs/adr/ADR-L04-https-secrets.md), [L05](../docs/adr/ADR-L05-backup-restore.md), [L08](../docs/adr/ADR-L08-operations-privacy.md).

Дополнительно: localization evidence для DB/Identity/files/logs/monitoring/backups/provider support, key management, отдельный актуальный erasure/revoke ledger, quarantine restore с reconciliation barrier, finite backup destruction и provider evidence. Incident timers/операторские документы/договоры по PD-ORGANIZATIONAL-DOCUMENTS.

Tests: snapshot до erasure и revoke, missing/stale ledger fail closed, latest-watermark race, copy deletion, alerts/incident tabletop; local production-config rehearsal, compose validation/image inspection, network ports/headers spoof, cookie Secure/CSRF via HTTPS, renewal/rotation, restart retention, migrate failure/rollback. Clean-host restore из off-host (не исходных volumes), checksums/history/Identity для правомерно сохраняемого набора после erasure reconciliation, объяснённые manifest удаления, synthetic write flow; revoke restored sessions и access reconciliation. Backup corruption/missing secret/failed transfer/low disk alerts получает реальный operator. RTO измеряет весь incident recovery, не только DB import.

Exit: signed-off operator evidence, C04 targets выполнены измерениями или gate возвращён владельцу, complete restore/rollback/recovery docs; domain/TLS/public boundary проверены на supplied host. Local-only implementation не называется deployed pilot. Risks: DNS/IPv6 exposure, unknown host performance, key loss, inconsistent snapshots, resurrected access после restore, large copy window. Никакого real-data launch до L4.

## L4 — Compliance & Pilot Acceptance

**Цель:** определить READY_FOR_PILOT либо NOT_READY по фактам выбранной среды/группы. Сложность **средняя/высокая**, ориентир 2–5 дней для выбранного доступного device scope, не включая устранение обнаруженных дефектов/ожидание hardware.

Prerequisites: accepted L1–L3, разрешение L4; C01–C09 закрыты для real pilot, назначены operator/testers и workload/device budgets. PD-G01–14 закрыты проверяемыми evidence до release: официальные редакции, выбранный уровень/меры, опубликованная политика/подписанные документы, уведомления, legal review. Неприемлемый C07 workaround требует отдельного scope. Erasure теперь явная зависимость L1–L3, а не optional после пилота.

Изменения: reuse K Playwright sequence с настоящими Identity accounts/CSRF/cookies и отдельными Student A/B, scoped/foreign Teacher; безопасный acceptance harness без production reset/dev-token; reports/evidence/runbooks/readiness matrix. Только доказанные scoped defect fixes отдельными reviewable commits, не расширение Domain.

Дополнительные tests/evidence: сквозные basis/consent/representative/withdrawal/block/access/correction/erasure/copies/restore, legal SLA и доказательства; R01–R20 applicability, privacy notice и назначения/допуски/обучение, квитанции РКН, поставщики/локализация/технические меры по принятой модели угроз; juridical sign-off с датой редакции.

Tests: все шесть экранов, 2 ordered real camera photos, immutable hashes, annotations обеих страниц, revision/new attempt/accept/history/reload; wrong roles/scopes/expiry/logout/reset/deactivation/cross-tab; CSRF/limits/restart. Повторить restore с оператором из L3 procedure и сверить RPO/RTO. Performance mix+concurrency/soak на candidate host с real-size photos/long history/large Lesson, peak RAM/CPU/disk/WAL/backup overlap, измерить p95/p99 и frontend latency по C06. Физические устройства и реальные OS/browser/input по [ADR-L07](../docs/adr/ADR-L07-device-browser-scope.md)/K protocol; missing hardware остаётся BLOCKED.

Exit: D5 запрещает READY_FOR_PILOT при любом обязательном compliance gap; legal sign-off не заменяет technical proof. Каждый release gate имеет evidence, operator/date/commit/environment; no unresolved P0/P1, выбранные quotas/recovery/privacy/device budgets выполнены. Owner явно принимает scope и старт real pilot. READY_FOR_PILOT не означает READY_FOR_PRODUCTION; failure любого обязательного gate → NOT_READY с next actions. Risks: small sample ≠ soak, Chrome emulation ≠ Safari/Pencil, green health ≠ restore, синтетические PNG ≠ реальные фото.

## Что не блокирует локальную подготовку и что блокирует release

В L0.1 разрешены только документы/проверки/commit. После отдельного разрешения L1 минимальный пакет L01-DECISION-GATES достаточен для согласованной синтетической разработки; одних C01/C02 уже недостаточно; C03/C05 не нужны для этих tests. В L2 можно проектировать/test механизмы с fixture values до получения host; production thresholds зависят C06/C03. L3 local config/restore rehearsal возможны заранее, но без owner infrastructure inputs нельзя закрыть deployment. L4 никогда не закрывает C08/C09 или physical evidence за владельца. Сроки выше — оценка инженерной сложности, не принятое обязательство.


## Приоритет L0.1 и оценки

Эта редакция включает lifecycle/erasure как обязательную часть будущего плана. Исторические оценки дней L0 выше относятся к прежнему техническому scope и **не включают надёжно оценённую стоимость compliance**; переоценить после PD-G02/03/06/08/09. Нельзя использовать их как срок закрытия юридических/провайдерских gates. Ни один L1–L4 этап не начат. [Compliance matrix](../docs/compliance/PD-COMPLIANCE-MATRIX.md) — обязательное приложение к L4, результат сейчас **NOT_READY / NOT VERIFIED**.
