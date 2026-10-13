# Sapientum LMS — roadmap завершения MVP

> **2026-10-12 supersession.** [Owner decision](TEACHER-REVOCATION-OWNER-DECISION.md) closes the physical-completion proof dependency and authorizes continuation of the current HTTP/React migration. Historical estimates and negative timing evidence below remain history; logical deadline/security and other product/operations gates remain.

2026-10-10. **AUDIT ONLY / OWNER REVIEW**. Основание: [inventory](MVP-READINESS-INVENTORY.md), [backlog](MVP-BACKLOG.md). Runtime, API, schema, migrations, ADR и register не изменены. Ниже предложенная последовательность; implementation authorization отсутствует.

## Решение о текущей готовности

Полный MVP пока не готов. Нет самостоятельного пути organization → admin → activated participants → published course → assignments. Существующий учебный цикл Submit → Review → Revise → Accept работает в legacy synthetic HTTP harness; интерфейсы фото/проверки/истории реализованы, но это не целевой authenticated browser E2E.

Inventory после owner supersession содержит 63 обязательные операции по ролям: **0 VERIFIED целевого scope, 17 IMPLEMENTED_NOT_VERIFIED, 39 PARTIAL, 7 MISSING, 0 BLOCKED**. Статус X14 стал PARTIAL: physical-only gate снят, но secure migration и E2E ещё не завершены. Это не процент написанного кода: shared dependencies повторяются в journeys. Существующие тесты сейчас прошли: **677 backend + 366 frontend**, TypeScript PASS; browser/physical device E2E NOT RUN. Новые findings не дают разрешения на cutover.

## Пять работ с наибольшим эффектом

1. **B07/B08/B09 — из Markdown в опубликованный учебный процесс.** Создать importer/validator/compiler, затем publication/open/assign и полный lesson/hints/reference. Сейчас snapshots существуют без пользовательского способа их получить.
2. **B01 + B02/B11/B12 — устранить интеграционный gate и замкнуть безопасный вход/доступ.** Это критический путь всех настоящих HTTP journeys; не подменять его dev bearer или сокращением security требований.
3. **B03/B04/B06 — дать администратору подготовить организацию, участников и назначения.** Переиспользовать S2/S3, не писать вторую модель ролей.
4. **B05 — активация/recovery/независимый operator до завершения onboarding.** Не откладывать lost access как необязательный production штрих: он входит в целевой MVP.
5. **B10/B13/B14 — входная навигация, базовый прогресс и возвращение к проверенным работам.** Малые видимые пробелы блокируют уже написанные экраны: known ID и UUID-справочник не пользовательский процесс.

## Предлагаемые implementation slices и critical path

| Slice | Минимальный результат | Зависимости / owner acceptance | Первый закрываемый сценарий |
|---|---|---|---|
| D0 — scope/contract acceptance | Уточнить R01–R04; подтвердить внешний Markdown, teacher-only reference/rubric, минимальный профиль и devices; отдельный bootstrap/publisher capability contract | Сохранить принятые решения; только новые scoped решения владельца | Не E2E; снимает неоднозначность объёма |
| P1 — B07a, 3–5 дней | Детерминированный validator/compiler внешнего пакета, file/line diagnostics, separate student/teacher artifacts, synthetic fixture tests | Новое узкое разрешение B07a; без HTTP/DB/publication/новых grants | Проверяемый content component; **не E2E-02 ACCEPTED** |
| G — B01, normative closed | Physical-completion proof cancelled; retain evidence without claiming hard-real-time guarantees | [Owner decision](TEACHER-REVOCATION-OWNER-DECISION.md); validate logical deadline/cancellation/no-late-success in each migrated path | Не блокирует HTTP migration; target E2E still requires secure composition and regression pass |
| I1 — B02/B11 | Session/browser integration и общий scoped learning boundary; rows/counts/history/current restrictions/transaction protocol | Current HTTP/React migration authorization + accepted S1/S2/S3/A1/A2 reuse | Session login/refresh/logout subset E2E-08; activation/recovery ещё впереди |
| A1 — B03/B04/B06/B10 | Bootstrap → org-admin → roster accounts → activation → course/group/enrollment/teacher assignment → catalogs | I1 + отдельные admin/challenge/schema contracts/S5 scope | **E2E-01**, базовая activation часть E2E-08 |
| A2 — B05 | Shared recovery, independent approval, block/unblock, code lifecycle/rates | A1, S6 authorization и G03 review | **E2E-08 полностью** вместе с I1 |
| P2 — B07b/B08/B09 | Import preview → immutable publish → open lesson → assign; lesson/hints/assets/reference по точной версии | P1 + A1 + I1; protected assets требуют F1; explicit publisher authority | **E2E-02** после готовности F1 для binary materials |
| F1 — B12 | Scoped upload/preview/claim/download/delete, logical deadline/no-late-success, immutable originals | I1; отдельное file/storage authorization; P2 asset contract согласовать до DTO | Доступные фото для E2E-03/04; единичный upload не end-to-end |
| L1 — B13/B14/B15 | Basic roster progress, history entry point, honest retry/unknown outcome | A1/P2/F1/I1 | **E2E-03 → 04 → 05**, regress E2E-06/07/09 |
| Q1 — B17/B18/B19/B20 | Discussion reply, reproducible ops/restore, bounded uploads/cleanup, selected physical devices | Respective contracts; ops/device preparation можно раньше; final drill на integrated system | Error/device/restore acceptance; не включает real pilot |
| V1 — B16 | Один чистый synthetic прогон всех ролей E2E-01–09; owner evidence review | Предыдущие slices; отдельный S8/M3 scope только где нужен; no legacy automatic backfill | **SYNTHETIC MVP ACCEPTED** только после явной приёмки |
| R — B22 | Real-data readiness legal/security/ops/freshness/erasure и отдельное разрешение | V1 + независимые gates ниже | **PILOT AUTHORIZED** — другое решение |

Dependency graph по результатам (не календарные обещания):

```text
D0 → B07a → B07b/B08/B09 ──────────────────┐
logical deadline contract + HTTP authorization → B02/B11 ─┼→ B12 → B13/B14/B15 → Q1 → B16
                              └→ B03/B04/B06/B10 ─┘
                                            └→ B05 → full E2E-08
B18 runbook и B20 device preparation ───────────────→ Q1 final acceptance
```

P2 и F1 имеют общий material contract, но не циклическое условие реализации: заранее принять DTO/parent contract; binary asset integration подключить после F1. B18 подготовка предшествует B16; финальные restore assertions входят в общую приёмку V1. Оценки этих этапов не складываются повторно с B-backlog.

Physical-completion G02 gate больше не блокирует migration. Текущая авторизация разрешает продолжить secure HTTP/React composition; synthetic tests всё ещё не заменяют logical deadline, authorization, privacy, restore, device и release acceptance.

## Текущий разрешённый slice

**M02 / B02+B11: продолжить secure HTTP/React migration и закрыть оставшиеся API regressions.** Не возвращать legacy teacher bypass ради зелёных тестов. Положительные teacher scenarios должны использовать scoped JWT, original logical deadline, current processing/resource authorization and durable attempt identity; отрицательные org/group/parent/revoke/replay paths должны остаться отрицательными.

Acceptance: login/refresh/switch composition; React in-memory access token and CSRF/Origin flow; migrated review/message/file calls with stable command/intent IDs; deadline cancellation and no-late-success; unknown COMMIT without automatic replay; full API/frontend regression with failures recorded. Physical syscall/COMMIT completion is explicitly not required.

## Что переиспользовать, что исправить

**Переиспользовать:** ProblemWork state machine, immutable Submission/Review/Message/history и exact CourseVersion references; work CAS/transactional events; ordered photo uploads/private originals/claim triggers; UI course/lesson/problem/review/queue/history; renderer для text/math; canvas/fullscreen; S1 schema, S2 mapping/session/O01, S3 generation-aware internal writer/reader; S4 component token/key/clock/current conservative gate. PRS1 I1 reusable parser остаётся pure component и не разрешает exact.

**Исправить при интеграции:** Program/ActorMapper/legacy AccessPolicy и queries на текущем GroupId; detached access checks перед Save; отсутствие current restriction SQL predicate и write serialization; upload без принятого org/parent/context admission; missing content body/hints/materials; root placeholder catalogs; отсутствие review-history entry point; session/cache/unknown-result coordination. Не переписывать уже рабочую state machine и не создавать новый annotation editor.

**Отложить:** CMS/WYSIWYG, advanced analytics, отдельный универсальный чат/расписание, большие экспортные системы, speculative distributed infrastructure. History pagination/content batching/canvas optimization — только по измеренному workload. PRS1 I2–I5 parked; exact matching не prerequisite при сохранённом conservative deny. Public signup не входит в scope. Произвольные файлы сверх текущих JPEG/PNG не добавлять без принятого формата; text/Markdown и A4 photo-first достаточны для базовой Submission.

## Owner decisions до затронутых работ

1. **Teacher Revocation timing:** owner accepted logical deadline and cancelled physical-completion proof. Validate cancellation/no-late-success and preserve unknown/replay safety; do not build supervisor/fencing solely for a hard physical bound.
2. **Новые административные возможности:** bootstrap platform operator, org-admin scope, independent security operator, teacher publication capability. Нельзя присвоить их Student/Teacher ролью по умолчанию.
3. **Контент:** подтвердить external authoring, teacher-only exact-version source/rubric, asset policy и contract migration. Старый K workaround нельзя считать выполнением нового требования без явного scope решения.
4. **Реальные devices/workload:** C07 (конкретные модели/browser/input/tester), C06/C08 для quotas/retention. Физический планшет не снимается с проверки молча.
5. **Profile/ops:** минимальные names/roster fields; bootstrap secret custody; для пилота дополнительно RPO/RTO/off-host custody/legal/freshness и независимая проверка.

## Разделение gates

| Gate | Component work synthetic | Полный synthetic HTTP E2E | Real-data pilot |
|---|---|---|---|
| PRS1 exact OFF / I2–I5 parked | Не препятствует pure/product work | Exact не обязателен; current conservative gate обязателен | Не включать exact без отдельного полного proof/разрешения |
| Teacher Revocation logical 15/30/60; physical gate cancelled | Компоненты проверяются | Не блокирует migration; logical deadline/security regression mandatory | Не является самостоятельным pilot blocker; прочие security/ops/legal gates остаются |
| HTTP/React migration authorized in current scope | Не ослабляет S2/S3/processing/privacy | Продолжать secure composition; target E2E requires green positive/negative regressions | Real-data pilot still requires separate security/legal/ops authorization |
| Не закончены admin/content/user journeys | Можно реализовывать только отдельно разрешённые slices | **Блокирует** E2E-01/02/03/08 | Блокирует |
| Real ProcessingRestrictions freshness, legal basis/minors/erasure | Контролируемые synthetic fixtures допустимы | Не требуют использования реальных данных для теста | **Блокирует**, отдельное решение и evidence |
| External human security audit / SR-01–04 | Компонентная регрессия не закрывает findings | Targeted integration proof/review обязателен; не объявлять full security acceptance | **Блокирует** release до требуемого закрытия |
| Physical device scope / production operations | Unit/headless возможны | Для заявленного tablet MVP нужны физические испытания; clean synthetic restore нужен | Все заявленные devices + qualified deployment/off-host restore required |

## Условия завершения MVP

Все обязательные A/T/S/X строки доведены до согласованного scope и проверены; P1 quality criteria включены в приёмку либо имеют явное owner disposition. Администратор подготавливает среду без SQL, activation/recovery/login работают, преподаватель публикует/назначает курс, ученик отправляет ≥2 фото и повторную попытку, учитель annotates/revises/accepts, обе стороны находят историю/progress через навигацию. E2E-01–09 проходят с настоящим разрешённым session/authorization boundary, negative org/group/owner/file/count assertions, concurrency/retry/expiry/restriction tests. Чистый restart/restore и заявленные devices подтверждены. Нет ручных dev bearer, скрытых seed/grants или direct DML как условия пользовательского успеха.

**Неизменяемость истории и security gate важнее формального зелёного smoke.** UI COMPLETE, E2E ACCEPTED, SYNTHETIC MVP ACCEPTED и PILOT AUTHORIZED фиксируются раздельно владельцем.

## Оценка остатка и следующий шаг владельца

Оценимые B02–B20: **74–129 человеко-дней** (64–109 P0 и 10–20 P1), около **15–26 инженерных недель** последовательной работы. Это диапазон планирования, не срок запуска: **B01 G02 не оценён**, ожидание решений/устройств, новые proof failures и real-data pilot не включены. Нет основания обещать завершение за несколько косметических UI задач.

Первое рекомендуемое решение — узкое разрешение **B07a** после review этого пакета; параллельное исполнение, новые worktrees или перенос dirty ветки этот roadmap не разрешает. Текущий task завершает только аудит. Все implementation действия остаются на последующее решение владельца.

**TEACHER-REVOCATION PHYSICAL GATE CANCELLED · HTTP/REACT MIGRATION AUTHORIZED TO CONTINUE · LOGICAL DEADLINE/SECURITY GATES ACTIVE · PRS1 EXACT OFF · MVP/PILOT NOT ACCEPTED.**
