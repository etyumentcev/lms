# L — Sequential implementation plan (L0.3)

2026-10-09. Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. L1–L4 не начаты и не разрешены. Приняты C01/C02 business и ORG-G01–04/06/07; роли/основания/сроки REQUIRE LEGAL REVIEW. [Реестр](L-DECISION-REGISTER.md), [readiness](L03-L1-READINESS.md).

## Общая стратегия

Canonical `/home/tea/github/lms`, последовательно, без новых worktrees/агентов. Актуальные инструкции: main, если пользователь отдельно не задаст ветку; перед каждым разрешённым этапом проверить HEAD/clean tree/base. Не предполагать интеграцию предыдущих веток и не выполнять push/merge/deployment без разрешения. L0.3 создаёт только documentation commit на явно запрошенной ветке.

Каждый этап от принятого predecessor и после отдельного разрешения. Исторические C–L0.2 reports и evidence сохраняются; K 366 frontend/260 backend — не свежий PASS. Проверки appropriate layer и реальный PostgreSQL/HTTP/browser; destructive runner никогда не real data.

## L1 — Identity, Organization Scopes & Access Lifecycle

Единственный детальный план: [L03-L1-IMPLEMENTATION-PLAN](L03-L1-IMPLEMENTATION-PLAN.md). Он supersedes прежний L1 «Identity & Personal Data Lifecycle» и operator-only provisioning. Разрешённый будущий scope: synthetic Identity→Actor, Organization/memberships/enrollment/teacher assignment, server scopes/file authorization, closed org-admin provisioning, code activation/recovery и session/revocation. T01–T06 PROPOSED требуют рассмотрения; после этого отдельное разрешение владельца.

Юридические consent/basis/representative/DSAR/production erasure workflows не включены. Версии ProcessingContext/current restriction contracts без фиктивных legal basis нужны для технической изоляции. ORG-G05/08 и PD gates блокируют эти правовые workflows/real data, но не сами synthetic auth/scopes после технического согласования.

Порядок slices: schema/migration → Identity mapping → membership/scopes → authorization (включая базовый revoke barrier) → provisioning → activation/recovery → hardening → integration/security acceptance. В плане указаны use cases/API/schema/tests/recovery и условия перехода. Exit L1 — доказанный synthetic packet, NOT_READY/NOT VERIFIED сохраняются; L2–L4 отдельно.

## L2 — Resource Protection & Data Lifecycle

**Цель:** bounded resource admission и безопасное обращение с pending/orphans. Сложность **высокая**, ориентир 4–8 дней после accepted L1 и workload/retention inputs.

Prerequisites: accepted L1, разрешение L2; C06 объём/квоты и C08 pending retention до финального product config/destructive cleanup. PD-G07/08 должны определить retention/destruction/holds и принять ADR-L10 до разрушительных механизмов. B07 technical enforcement можно проверить на fixture thresholds; незакрытые C06/C08 не разрешают выбрать реальные квоты или удалять real files.

Изменения: API rate/concurrency/timeout/body bounds, upload early admission; durable budgets/reservations migration и disk reconciliation; quota startup validation; pending expiry/row-lock claim/tombstone coordination, idempotent cleanup lease/retry/quarantine; errors/Retry-After и frontend понятные failures без retries команды. Existing MaxBytes/annotation constraints сохраняются. Подготовить interface maintenance barrier для backup L3. Точки: AttachmentsController, AttachmentService, IAccessPolicy/transactional claim boundary, LocalFileAttachmentStorage, Program; все новые schemas только technical resources. [ADR-L06](../docs/adr/ADR-L06-resource-protection.md).

После отдельного legal review и разрешения соответствующего workflow (не автоматический scope L1/L2): реализовать privileged erasure workflow ADR-L10, retention по категории/цели, manifest и audit evidence; обычный orphan cleanup никогда не удаляет linked history. Работа с файлами/metadata/DomainEvents/annotations/смешанными субъектами — отдельные проверяемые шаги, app role без bypass.

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

Prerequisites: accepted L1–L3, разрешение L4; C01–C09 закрыты для real pilot, назначены operator/testers и workload/device budgets. PD-G01–14 закрыты проверяемыми evidence до release: официальные редакции, выбранный уровень/меры, опубликованная политика/подписанные документы, уведомления, legal review. Неприемлемый C07 workaround требует отдельного scope. Erasure — обязательная проверяемая зависимость до real-data пилота после правовых решений; оно не входит автоматически в synthetic L1.

Изменения: reuse K Playwright sequence с настоящими Identity accounts/CSRF/cookies и отдельными Student A/B, scoped/foreign Teacher; безопасный acceptance harness без production reset/dev-token; reports/evidence/runbooks/readiness matrix. Только доказанные scoped defect fixes отдельными reviewable commits, не расширение Domain.

Дополнительные tests/evidence: сквозные basis/consent/representative/withdrawal/block/access/correction/erasure/copies/restore, legal SLA и доказательства; R01–R20 applicability, privacy notice и назначения/допуски/обучение, квитанции РКН, поставщики/локализация/технические меры по принятой модели угроз; juridical sign-off с датой редакции.

Tests: все шесть экранов, 2 ordered real camera photos, immutable hashes, annotations обеих страниц, revision/new attempt/accept/history/reload; wrong roles/scopes/expiry/logout/reset/deactivation/cross-tab; CSRF/limits/restart. Повторить restore с оператором из L3 procedure и сверить RPO/RTO. Performance mix+concurrency/soak на candidate host с real-size photos/long history/large Lesson, peak RAM/CPU/disk/WAL/backup overlap, измерить p95/p99 и frontend latency по C06. Физические устройства и реальные OS/browser/input по [ADR-L07](../docs/adr/ADR-L07-device-browser-scope.md)/K protocol; missing hardware остаётся BLOCKED.

Exit: D5 запрещает READY_FOR_PILOT при любом обязательном compliance gap; legal sign-off не заменяет technical proof. Каждый release gate имеет evidence, operator/date/commit/environment; no unresolved P0/P1, выбранные quotas/recovery/privacy/device budgets выполнены. Owner явно принимает scope и старт real pilot. READY_FOR_PILOT не означает READY_FOR_PRODUCTION; failure любого обязательного gate → NOT_READY с next actions. Risks: small sample ≠ soak, Chrome emulation ≠ Safari/Pencil, green health ≠ restore, синтетические PNG ≠ реальные фото.

## Зависимости и release gate L0.3

L2 зависит от принятого L1; общая quota/cleanup защита не откладывает базовую auth rate-limit/file scope/commit guard из L1. Юридически значимые retention/erasure механизмы требуют PD-G02/07/08/09 и отдельного workflow scope; если решения отсутствуют, они остаются BLOCKED, не заполняются тестовыми основаниями. L3 зависит от безопасного ресурсоёмкого контура L1/L2 и C03–05/C09; production restore должен учитывать актуальные revoked/erased records до ingress. L4 требует весь legal/technical/operational/device evidence packet, ORG-G05/08 и PD-G01–14. Ни L1 completion, ни подпись архитектурного ADR не закрывает пилот.

В прежних документах оценки инженерных дней не включали полный compliance scope; новые сроки не обещаются до T01–T06 и правовых процедур. [L03 readiness](L03-L1-READINESS.md) — актуальная точка следующего разрешения. STOP после L0.3 commit.
