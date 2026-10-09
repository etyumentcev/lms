# Codex execution task — L1 (PREPARED, NOT AUTHORIZED)

Этот документ — задание для будущего отдельно разрешённого этапа. Его наличие, ссылка или завершение L0.3 **не разрешают исполнение**. В текущем checkpoint не выполнять.

## Предпосылки

1. Получить явное разрешение владельца L1 с scope **SYNTHETIC DATA ONLY** и проверить актуальные [readiness](L03-L1-READINESS.md)/[owner decisions](L03-OWNER-DECISION-REGISTER.md).
2. Зафиксировать решения либо явное делегирование E01–E06 до affected slices; не считать PROPOSED варианты принятыми. Emergency и privacy restrictions не решены ordinary двухчасовым отзывом.
3. Работать последовательно в `/home/tea/github/lms`, main по актуальным AGENTS.md, если владелец не укажет ветку; без новых worktrees/параллельных агентов. Проверить HEAD/ветку/clean tree, подтвердить наличие документационного commit и фактическую базу; не делать merge автоматически. Чужие изменения не переписывать/не коммитить.
4. Прочитать ADR-L01/L02/L11–L15, domain/feature/UI contracts, [детальный план](L03-L1-IMPLEMENTATION-PLAN.md), PD/ORG gates. Сохранять существующие checkpoints.

## Scope и порядок

Реализовать только принятую architecture .NET/EF Core Identity + signed JWT, stable Actor, organization/membership/course/group sidecars, closed org-admin UI, T01 activation/recovery, versioned ProcessingContext и file/operation authorization. Шесть основных учебных экранов, aggregate transitions, existing IDs/history/content/annotations сохраняются.

Выполнять S1 schema/M1–M2 и M3 rehearsal → S2 Identity/JWT → S3 grants/scopes → S4 все authorization/files/restrictions/deadlines → S5 org-admin provisioning → S6 activation/recovery → S7 long operations/security/browser → S8 integration и controlled synthetic M3 cutover. Цели/зависимости/изменения/инварианты/tests/acceptance каждого slice заданы планом, все обязательны. Не оставлять незащищённые paths до S7; не открывать ingress до S8.

## Обязательные критерии

- T01: 16 Crockford Base32/80 бит, группы по четыре; activation 24h/recovery 15m; 5 неудач/код; проверки 10/min/IP и 5/15min/account; выдача 3/hour/recipient, 20/hour/initiator, cooldown 60s; только HMAC-SHA256 с отдельным ключом и rotation runbook; пароли 12–128, no secrets audit.
- T02: минимальный org-admin UI, отдельные account/membership/enrollment/teacher assignment; controlled first admin; no public signup/mandatory email/phone; одноразовый показ кода после verification, independent shared recovery, no admin reader/self-escalation.
- T03: accepted tables, unique user↔Actor/stable UUID, multi-org, one course owner/unknown quarantine, Generation/no revived grants, DB constraints/no history cascade.
- T04: signed JWT ≤15 минут, local validation; fresh authority on issue/refresh, scoped AccessValidUntil ≤ request+2h, exp=min(Now+15m,AccessValidUntil−10m); insufficient lifetime deny. A revoke не лишает B. API 60s/max HTTP 8m; cancel/rollback/uncertain commit и bounded stream/jobs по ADR-L15. Не вводить ordinary PG barrier/Redis/per-request freshness как обязательство. Emergency реализуется только по отдельно принятому E03.
- T05: immutable versions, interval course binding/same org, historical refs/current restrictions, SyntheticOnly; no fake legal basis.
- T06: additive M1–M3, explicit synthetic manifest, IDs/history/hash preserved, quarantine и tested backup/restore/cutover; no automatic owner/grants.

## Tests и evidence

Выполнить матрицу §8 плана: unit/domain/application, реальный PostgreSQL constraints/races/restore, настоящий HTTP auth/expiry/rates/CSRF, browser six-screen/no replay/cross-tab. Synthetic A/B, shared account, outsider, OA/OB/P, same local group names, direct IDs/SQL totals/files/annotations. Fake-clock deadlines и actual slow I/O/DB commit/cancel tests; job reauthorization, NAT/code races/rotation. Targeted checks по slice, full build/regression в S8. Записывать scenario/result/commit/environment; NOT RUN не PASS, старые K tests не новый evidence.

## Данные и migrations

Только явно synthetic fixture/test DB и generated files; никаких реальных roster, email, телефонов, фото людей, consent/договоров/представителей. Empty DB без backfill. Synthetic legacy explicit mapping после backup/restore rehearsal. Unknown/real данные → STOP без автоматического переноса. Migrations только после отдельного L1 разрешения, на подтверждённых isolated empty/synthetic targets; T06 не разрешает real DB. Не изменять применённые migrations; не запускать startup migrate на неизвестном connection string.

M3 cutover отдельно контролировать: maintenance, отсутствие старых writers, validation всех constraints/auth paths, history/hash verification и negative smoke до ingress. Failure → maintenance/forward repair либо verified offline restore; no destructive Down. Согласовать DB/files/keys manifest, не обещать их общую transaction; restore не возрождает revoked grants/secrets. Production RPO/RTO/retention не назначать по synthetic fixtures.

## Stop conditions

Нет явного L1 разрешения; unresolved E вопрос нужен текущему slice; обнаружены real/unknown data или ambiguous ownership; конфликт approved architecture; невозможно доказать T04 deadline/cancellation либо T05 freshness; destructive history/schema change; missing restore evidence; failing critical security/isolation test. Зафиксировать blocker, не обходить grants/privacy/quarantine и не выдавать partial за complete. Чужие изменения отделять. Никаких push/merge/deploy/real pilot/L2–L4 без отдельного разрешения.

## Commit и отчётность

Делать reviewable commits по завершённым slices согласно новому разрешению; включать только свои изменения, `git diff --check` и appropriate tests до commit. Обновлять checkpoint/evidence без секретов. Итог: HEAD/branch/commits, implemented scope, tests с NOT RUN/FAIL, migration targets и restore evidence, OPEN DECISIONS/risks, readiness. Compliance status: NOT VERIFIED; Pilot readiness: NOT_READY; LEGAL SOURCE NOT VERIFIED до подтверждающих оснований. После L1 report остановиться; ни публикации, ни следующий этап не подразумеваются.
