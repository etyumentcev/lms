# M1 — локальное тестовое развёртывание Docker Compose

Дата: 14.10.2026

Статус: **TEST ENVIRONMENT ONLINE · PARTIAL — ZIP IMPORT BLOCKED · SYNTHETIC ONLY**

## Результат и граница

В каноническом checkout на `main` развёрнут локальный проект `lms-m1-local`:

- production React bundle на `https://localhost:8443`;
- HTTPS nginx → HTTPS ASP.NET API;
- PostgreSQL 17.6 с полной цепочкой из 17 EF migrations;
- Keycloak 26.8.1 в production profile с отдельной PostgreSQL database;
- реальный OIDC platform-operator token с `sub`, exact audience
  `sapientum-platform` и scope `organization:bootstrap`;
- generated local CA, service certificates, database passwords, challenge key и
  component signing key в отдельном private Docker volume;
- persistent private volumes для PostgreSQL, attachments, content-import storage и
  ASP.NET data-protection keys;
- production UI без `local-login.html`, dev bearer adapter, deterministic seed,
  demo routes и placeholder navigation.

Старые volumes проекта `lms-local` не переиспользовались и не удалялись. Реальные
детские/production данные не использовались. Commit/push до отдельного запроса не
выполнялись; этот отчёт подготовлен для разрешённого пользователем commit.

## Actor → Use Case → Invariants → Entities/VO → transitions → authority/data → tests

| Область | Фактическая граница |
|---|---|
| Actors | local platform operator; organization administrator; CourseEditor/teacher; student |
| Use cases | первый bootstrap организации, активация/login, M1 administration and learning paths через production UI/API |
| Invariants | org/course/group/parent и роли устанавливает сервер; exact Origin + CSRF; no anonymous/dev token; private files; immutable history; no client authority |
| Entities/VO | существующие Organization/User/Actor/Course/Group/ProcessingContext/ContentImportAttempt/ProblemWork; новых domain entities или migrations нет |
| State transitions | empty environment → organization/admin activation → real session; существующие Draft → Ready → Published и NotStarted → Submitted → NeedsRevision → Submitted → Accepted не изменялись |
| Authorization/data source | Keycloak только для platform bootstrap; S2/S3/S4 component sessions и PostgreSQL остаются authoritative для application paths; secrets только из private volume |
| Tests | clean Compose boot, migration count, TLS health, JWT claims, positive contract admission, negative anonymous/Origin/CSRF, production bundle scan, dev-token denial, API restart persistence; importer fail-closed recorded separately |

Новые business writes, transaction semantics, lock order, CAS, audit, retry или
unknown-outcome protocol не вводились. Bootstrap, administration, publication и learning
writes используют существующие принятые реализации. Compose migration и identity database
initialization идемпотентны; application data owner-backed deployment profile остаётся
ограничением ниже.

## Изменённые пути

- `docker-compose.yml` — изолированный M1 project, service ordering, networks, volumes,
  HTTPS, Keycloak, migration и operator-token profile;
- `docker/nginx.conf` — TLS gateway, verified TLS к API и security headers;
- `docker/local-init/*` — idempotent secrets/CA/certificate/realm generation;
- `docker/keycloak/sapientum-local-realm.template.json` — local synthetic realm,
  platform API audience, `basic` subject mapper и bootstrap scope;
- `docker/init-identity-db.sh` — отдельные idempotent Keycloak role/database;
- `docker/operator-token/*` — получение реального short-lived Keycloak token;
- `src/Lms.Api/Dockerfile` — pinned Python/Node/KaTeX runtime dependencies;
- `src/Lms.Api/TeacherRevocationComposition.cs` — persistent Data Protection keys и
  explicit platform JWT audience;
- `src/Lms.Web/src/App.tsx` — production runtime без demo/placeholder/harness opt-in;
- `README.md` — актуальный runbook запуска, первого bootstrap, persistence/reset и
  честная граница importer runtime.

## Исполненные проверки

| Проверка | Команда / фактический результат |
|---|---|
| Compose model | `docker compose config --quiet` и `docker compose --profile tools config --quiet` — PASS |
| Build/start | `docker compose up --build -d` — PASS; Web Vite production build: 302 modules, API `dotnet publish` PASS |
| Runtime status | `docker compose ps -a` — postgres/identity/api/web healthy; init/migrate jobs exit 0 |
| HTTPS | `curl --cacert /tmp/sapientum-m1-local-ca.crt https://localhost:8443/api/health` — HTTP 200 |
| OIDC positive control | real Keycloak token contains non-empty `sub`, exact platform audience, bootstrap scope and future `exp`; authenticated malformed bootstrap reaches contract validation — HTTP `400 ValidationFailed` |
| Negative access | no platform token — 401; valid token + wrong Origin — 403; login without CSRF — 403 |
| No dev auth | `dotnet Lms.Api.dll --dev-token` inside API — rejected because `LocalDemo:Enabled` is false |
| Production UI | runtime bundle/files contain no `lmsAttachmentHarness`, demo navigation, `lms-local-token`, `local-login.html` or `local-auth.js` — PASS |
| Frontend regression | `pnpm --dir src/Lms.Web test` with bundled Node — 19 files, 421 tests PASS; four obsolete demo assertions were replaced with no-demo/fail-closed/DEV-only assertions |
| Restart persistence | API restart returns healthy; data-protection key digest unchanged; EF migration history remains 17 rows |
| Clean first boot | temporary project `lms-m1-clean-test` from empty volumes — API healthy, 17 migrations, clean realm token claims PASS, authenticated contract response `400 ValidationFailed`; temporary containers/networks/volumes then removed |
| Import dependencies | Python PyYAML/markdown-it/Pillow, Node 24.19.0 and KaTeX 0.16.47 pins — PASS |
| Import execution | valid M1 ZIP in ordinary API container — expected fail-closed `Failed/I_IMPORT`, no receipt/student/teacher artifacts because qualified systemd/cgroup/bubblewrap control plane is unavailable |

Полная Domain/Application/Infrastructure/API regression и full Chromium M1 journey в этой
deployment-задаче **NOT RUN**; frontend regression выполнена отдельно, а прошлый backend/browser
acceptance не переименован в новый PASS. Проверка чистого Compose не создавала application
organization/users/courses и не раскрывала secrets.

## Import runtime blocker

Accepted B07b использует `/usr/bin/systemd-run --user`, user systemd manager, writable
delegated cgroup v2 controllers, bubblewrap и unprivileged user namespaces. Обычный
unprivileged Docker container предоставляет read-only cgroup namespace и не содержит этот
qualified control plane. Добавить unisolated fallback запрещено контрактом B07b.

Два возможных продолжения требуют отдельного owner/security решения:

1. nested systemd + cgroup delegation/capabilities для local-only API container;
2. отдельный изолированный import worker и новый доверенный internal protocol.

До решения ZIP import/publication через этот Compose profile не считается работоспособным.

## Guardrails §7

- **Client authority / IDOR:** covered для новых bootstrap wiring — реальный JWT,
  server validation, negative anonymous/origin; application IDOR paths не менялись.
- **Cross-org/course/group/assignment/role:** NOT RUN — новые access paths не добавлены;
  существующая full M1 regression не перезапускалась.
- **Missing/forged parent, moved/revoked membership, restriction narrowing:** NOT RUN —
  deployment wiring не меняет соответствующие contracts.
- **Post-pagination/count leakage:** not applicable — новые list/read endpoints отсутствуют.
- **File-parent bypass/inaccessible attachments:** not applicable к изменённому gateway;
  private volume и отсутствие host port covered, application negative tests NOT RUN.
- **Stale authorization across transactions, mutable history, duplicate effects,
  unknown outcome:** not applicable — use-case/persistence code не менялся.
- **Secrets:** covered — generated values только в private named volume, в Git/log/report
  не записаны; public CA не является secret.
- **Unsupported readiness:** covered — importer blocker, owner-backed DB capability,
  synthetic clock и data-protection-at-rest limitation перечислены явно.

## Открытые риски и readiness

- `TeacherRevocation:RestrictedDatabaseCapabilities=false`: Compose использует schema owner;
  complete least-privilege deployment profile не квалифицирован.
- Data Protection XML keys persistent, но не зашифрованы внутри private Docker volume.
- Synthetic development clock включён; G02 остаётся BLOCKED.
- ZIP importer control plane BLOCKED, поэтому требование полного M1 без заглушек ещё не закрыто.
- Backup/restore, retention, monitoring, incident response и pilot browser visual acceptance
  этим развёртыванием не квалифицированы.

**TEST ENVIRONMENT ONLINE · UI/API/OIDC/DB COMPOSE PASS · ZIP IMPORT BLOCKED ·
UI COMPLETE NOT CLAIMED · E2E ACCEPTED NOT RE-RUN · SYNTHETIC MVP ACCEPTED UNCHANGED ·
PILOT NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · GENERAL S4 HTTP NOT AUTHORIZED.**
