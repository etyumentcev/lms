# L0 — Architecture & Decision Gates: results

**L0 выполнен как documentation-only анализ. Pilot/production: NOT_READY.** L1–L4 не реализованы. Следующее действие — рассмотрение результатов владельцем и ответы на минимальные gates L1.

## 1. Branch / base / commit

Canonical checkout `/home/tea/github/lms`. Начальное дерево чистое, `codex/k-release-hardening` HEAD `c7a500f56f26d94e3249a8c8a87e76018b0f5683`, как в запросе. Создана `codex/l0-architecture` непосредственно от этого commit. Другие ветки не изменялись, дополнительных worktree нет. Explicit branch instruction L0 применена вместо общего main default.

Result commit — коммит, содержащий этот отчёт; получить точный hash: `git log -1 --format='%H %s' -- codex/L0-RESULTS.md`. Hash сообщён в итоговом ответе после commit; self-referential hash внутрь коммита не записывается. Push/merge/deployment не выполнялись.

## 2. Источники

Все 13 обязательных файлов присутствуют и изучены; отсутствующих обязательных источников нет:

- [ExecPlan](EXECPLAN-LMS-SIX-SCREENS.md).
- [K results](K-RESULTS.md), [readiness](K-RELEASE-READINESS.md), [security](K-SECURITY-AUDIT.md), [test matrix](K-TEST-MATRIX.md), [performance](K-PERFORMANCE-REPORT.md), [device protocol](K-DEVICE-TEST-PROTOCOL.md), [MVP gaps](K-MVP-GAPS.md).
- [Domain Spec](../docs/domain/LMS-DOMAIN-SPEC.md), [Feature](../docs/features/FEATURE-SUBMISSION-REVIEW.md), [Frontend contract](../docs/ui/LMS-FRONTEND-CONTRACT.md), [UI Flow](../docs/ui/LMS-UI-FLOW-SPEC.md), [README](../README.md).

Исследованы Domain/Application ports/use cases, Infrastructure DbContext/access/queries/store/storage/migrations, API Program/ActorMapper/три controllers/LocalDevelopment, Web auth transport/App/main, Dockerfiles/Compose/nginx/local-login, K E2E runner и сценарий, tests и K evidence. Точные component links в [architecture](L-ARCHITECTURE.md) и ADR. Кода Identity, profiles, quota ledger и pilot backup не найдено: их proposed names явно помечены.

Внешние primary references для framework semantics: [Microsoft Identity SPA](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0), [Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [proxy](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [PostgreSQL dump](https://www.postgresql.org/docs/17/backup-dump.html)/[PITR](https://www.postgresql.org/docs/17/continuous-archiving.html), [Caddy HTTPS](https://caddyserver.com/docs/automatic-https). Они поясняют предложения, не доказывают наличие этих настроек в LMS. Library versions/config окончательно проверить в разрешённой реализации.

## 3. Подтверждённая архитектура

FACT: validated JWT principal → ClaimsActorMapper → Actor → existing enrollment/teacher scopes → Domain/work store. Identity subject сейчас ожидается internal UUID; IdentityUserId нельзя подставить вместо ActorId. Student/Teacher profile stores отсутствуют. History/content append-only, annotations JSONB, photos separate filesystem; snapshots базы и photos требуют общего barrier. Current Compose — Development с публичным demo key и loopback port, не защищённый pilot deployment. Web ожидает bearer adapter и не имеет полного production session lifecycle.

## 4. Документы

| Созданный документ | Содержание |
|---|---|
| [ADR-L01](../docs/adr/ADR-L01-identity-integration.md) | Cookie/bearer, claims/Actor mapping, schema/migration/session/CSRF |
| [ADR-L02](../docs/adr/ADR-L02-account-provisioning.md) | Operator/invitation/self-signup, activation/recovery/scopes/deactivation |
| [ADR-L03](../docs/adr/ADR-L03-vps-deployment.md) | Existing Compose, pilot topology/capacity/deploy/migrate/rollback |
| [ADR-L04](../docs/adr/ADR-L04-https-secrets.md) | Proxy/TLS/DNS/headers/cookies/DP keys/secrets |
| [ADR-L05](../docs/adr/ADR-L05-backup-restore.md) | Coordinated set, manifest/checksums/encryption/off-host, restore drill/RPO options |
| [ADR-L06](../docs/adr/ADR-L06-resource-protection.md) | Threat/enforcement/config/race/invariant matrix, durable quotas/cleanup |
| [ADR-L07](../docs/adr/ADR-L07-device-browser-scope.md) | Desktop/iPad/Android scopes и real-device gate |
| [ADR-L08](../docs/adr/ADR-L08-operations-privacy.md) | Operators/runbooks/privacy/retention/exceptional deletion gates |
| [L-DECISION-REGISTER](L-DECISION-REGISTER.md) | 3 accepted, 9 technical proposals, 9 открытых owner gates |
| [L-ARCHITECTURE](L-ARCHITECTURE.md) | Source-backed layer/endpoint/schema map |
| [L-IMPLEMENTATION-PLAN](L-IMPLEMENTATION-PLAN.md) | Последовательные L1–L4, prerequisites/tests/exit/risks/branches |
| [L0-RESULTS](L0-RESULTS.md) | Этот отчёт и вопросы владельцу |

## 5–7. Принятое, предложенное и открытое

**ACCEPTED только D1–D3:** свой VPS/сервер, Docker Compose, ASP.NET Core Identity — решения пользователя, не результат выбора Codex. Критического противоречия текущей архитектуре не найдено.

**PROPOSAL B01–B09:** same-origin secure cookie + CSRF; независимый Identity→Actor mapping; server-checked session lifecycle; private single-host topology с TLS edge; persistent protected keys; coordinated offline-write backup; edge/app/DB resource controls; real-device evidence; минимальные runbooks/logs. Эти рекомендации можно технически принять в будущих разрешённых этапах, но L0 их не реализовал.

**DECISION REQUIRED C01–C09:** provisioning, initial access/recovery, server/domain, RPO/RTO/window, off-host backup/key custody, workload/quotas, pilot group/devices/material workaround, privacy/retention, operators. UNKNOWN конкретные параметры; owner gates не закрыты.

## 8. Критические риски

- Подмена исторического ActorId Identity UUID, автоматический backfill неизвестных людей и grants; исключить явным mapping/roster.
- Cookie без CSRF, stale account role/session и данные предыдущего ученика в UI; нужны per-request checks и remount.
- Upload/annotation/concurrency истощают RAM/disk несмотря на per-file caps; нужны reservations/limits/measurements.
- DB-only/file-only backup и отсутствующие ключи делают restore неполным; восстановленный snapshot может вернуть отозванные права.
- Неизвестные RPO/RTO/operator/retention/privacy для работ несовершеннолетних; deactivation не erasure. Cleanup не должен нарушать immutable history.
- Safari/Pencil/Android hardware не проверены; K performance single caller не сертифицирует VPS, dense canvas и Lesson N+1 остаются.

## 9–11. Следующие этапы и границы разрешения

L1 Identity → L2 Resource Protection → L3 Deployment & Backup → L4 Pilot Acceptance. Детальные изменения/tests/exit/estimates/merge order — [план](L-IMPLEMENTATION-PLAN.md). Каждый этап новая отдельная разрешённая ветка после accepted predecessor, последовательно.

**Без дополнительных owner policy решений** можно технически обосновать B решения, подготовить локальные synthetic проверки/migration design. **Но текущее разрешение заканчивается L0:** никакой код L1 сейчас не разрешён. После отдельного разрешения L1 и C01/C02 можно реализовать Identity на synthetic data без домена/VPS/SMTP, если выбран manual recovery. C03–C09 не нужно требовать заранее ради local development.

**Нельзя до gates:** выбирать onboarding/recovery за владельца; создавать real learner accounts/данные до privacy/ops; устанавливать product quotas/retention без C06/C08; выполнять реальное hosting/DNS/backup destination без C03–C05/C09; заявлять device support без C07/evidence; объявлять readiness до L4. Любое требуемое privacy erasure или profile enrichment вне минимального scope сначала получает отдельное решение.

## 12. Вопросы владельцу

### Минимум для запуска L1 — два ответа

1. **Выдача аккаунтов (C01):** подтверждаете закрытую пилотную группу с созданием accounts оператором через CLI, одной ролью Student или Teacher и явными course/group правами? Рекомендация — да. Нужен уполномоченный issuer; это избегает публичной регистрации, но добавление людей зависит от оператора.
2. **Первый вход и восстановление (C02):** подходит одноразовый activation/reset code, переданный оператором после проверки личности через согласованный личный канал, без обязательного email/SMTP? Рекомендация — да для малого пилота. Укажите канал и кто помогает пользователю; альтернатива email recovery требует SMTP и расширяет L1.

После этих ответов отдельно разрешить L1; остальные вопросы можно закрывать к соответствующему этапу.

### До L2 и capacity planning

3. **Нагрузка и хранение (C06, часть C08):** сколько учеников/учителей и одновременных пользователей ожидается, сколько заданий/фото/попыток, как долго нужны несданные uploads и сами работы? Рекомендация — небольшой явно ограниченный пилот; численные квоты получить из этого сценария и измерений, не выбрать наугад.

### До L3

4. **Среда и эксплуатация (C03/C09):** какой сервер/ОС и домен доступны, кто имеет DNS/SSH и будет основным оператором/заместителем? Рекомендация — один сервер/один origin; ресурсы после load test. Нужен проверенный канал alerts, иначе сбой может остаться незамеченным.
5. **Восстановление (C04/C05):** сколько работ по времени допустимо потерять и какой простой/окно backup допустимы; где хранить encrypted off-host copies и кто хранит recovery key? Рекомендация — простой coordinated backup, если допустим перерыв записи; варианты от суток до минут RPO и их стоимость в ADR-L05. Точные цели подтверждает restore drill.

### До реальных данных и L4

6. **Устройства/группа (C07):** достаточно ли desktop Chromium или нужны iPad/Pencil/Safari либо Android; кто выполнит физический тест? Рекомендация — desktop-only лишь если фото/проверка на нём удобны реальной группе. Подтвердите временный secure roster по IDs и внешние материалы учителя; иначе нужен отдельный enrichment scope.
7. **Privacy (C08):** кто отвечает за данные и какие страны/правила применимы к ученикам, серверу и backup; какие сроки хранения, информирование/согласия и процедура удаления нужны? Рекомендация — минимальные данные и конечные сроки по категориям до real accounts. Immutable history требует отдельной процедуры исключительного удаления; соответствие закону пока не заявляется.

## 13. ExecPlan и проверка

Добавлен §27 L0 с boundary и планом L1–L4; обновлён текущий status в начале. История C–K не переписана. Проверка согласованности: D1–D3 vs proposals/owner gates, Domain immutability/state machine/scopes, источники components/13 endpoints/5 migrations, compatibility и dependencies между ADR. Проверены локальные Markdown targets новых документов, обязательные файлы, permitted diff и сохранение исторического ExecPlan; `git diff --check` без замечаний. App tests не запускались: production code запрещён к изменению, 366/260 — baseline K, не новый PASS L0.

Изменены только 12 новых Markdown документов и ExecPlan. После commit проверено чистое дерево. Начальная sandbox ошибка `mountinfo path is not absolute` обойдена одобренными escalated operations в canonical checkout. Один write был остановлен сбоем automatic approval из-за usage limit; после просьбы пользователя продолжить тот же approval путь успешно выполнен. Никаких обходов policy/implementation/deployment.

**Остановка после L0. Следующее действие — согласование результатов с владельцем.**
