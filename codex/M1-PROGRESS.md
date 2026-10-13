# M1 progress

Актуально: 13.10.2026.

| Этап | Статус | Фактическая цепочка / evidence | Следующая граница |
|---|---|---|---|
| M1-01 — browser authorization | **DONE** | React login/session UI → защищённые login/refresh/switch/logout → S2/S3/S4 → PostgreSQL runtime role; два synthetic users, reload, expiry, tabs, replay/CSRF/Origin/revoke/network/logout; `M1-01-RESULTS.md` и `evidence/M1-01-BROWSER-AUTH/` | component acceptance; не onboarding |
| M1-02 — protected teacher HTTP | **DONE** | существующий Teacher Revocation cutover + закрытая M1-01 dependency/runtime audit; full API 375 PASS, Frontend 411 PASS | не считать полным M1 E2E |
| M1-03 — organization bootstrap | **DONE (component acceptance)** | production React → platform JWT (`sapientum-platform` / `organization:bootstrap`) → atomic PostgreSQL bootstrap receipt/org/identity/membership/challenge/audit → one-time activation → обычный login → org-admin dashboard; `M1-03-RESULTS.md`, runbook и 4 browser screenshots | не переходить к M1-04 без отдельного задания; isolated `c2h_runtime` bootstrap-role profile остаётся deployment qualification |
| M1-04 — accounts, activation & recovery | **DONE (component acceptance)** | production React accounts list/search/filter/card → authorized API → atomic IdentityUser/Actor/membership/profile/existing AccessChallenge/audit → activation/password/first login; independent recovery и shared-identity approval; `M1-04-RESULTS.md` и 6 browser screenshots | не переходить к M1-05; isolated runtime capability, operator IdP operations и legal gates остаются deployment qualification |
| M1-05 — groups, courses, assignments & catalogs | **DONE (component acceptance, synthetic-only)** | production React admin course/group catalog → protected API → existing OrganizationMembership/EnrollmentScope/TeacherAssignmentScope; 12 students, teacher assignment, move/remove/unassign, immutable assignment affiliation, teacher/student catalogs, shared-actor org switch; `M1-05-RESULTS.md` и 10 browser screenshots | не переходить к M1-06; publication/import/content отсутствуют, isolated runtime capability остаётся deployment qualification |
| M1-06 — content import & publication | **DONE (component acceptance, synthetic-only)** | production React `Материалы` → protected ZIP upload → existing B07a/B07b → private ImportAttempt + diagnostics/role previews → explicit immutable v1 → lesson №1/G1/five exact-version assignments → immutable v2; historical assignments remain v1; `M1-06-RESULTS.md` и 7 Chromium HTTPS screenshots | не переходить к M1-07; production DB capability/cutover и G02 остаются blocked |

## Последняя проверенная цепочка M1-06

`LearningCatalog` → `CourseMaterialsPage` → HTTPS/Origin/CSRF + validated component bearer → whole-course S3 publisher scope + current processing gate → private `ContentImportAttempt` → существующий isolated B07b/B07a → раздельные student/teacher artifacts → explicit `PublishedContentVersion` → `LessonGroupOpening` + пять `ProblemAssignment` на exact v1. Затем v2 опубликована отдельно; DB и student React подтверждают, что прежние назначения остались на v1. Student API дополнительно фильтрует только открытые lesson/problem/assets и не сериализует teacher solutions/rubrics/hashes.

Никаких commit, push или deploy. M1-07 не начат.
