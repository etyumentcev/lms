# MVP-B07b-C2-J — isolated identity provisioning

2026-10-11. **ISOLATED IMPLEMENTATION — OWNER ACCEPTANCE PENDING.**

Основание: OWNER DECISION текущей задачи принимает C2-I с findings, H01/H02 accepted, авторизует внутреннюю C2-J capability, оставляет H04 будущему import writer. Это решение имеет приоритет над историческим заголовком C2-I. Production cutover, действующая БД, HTTP/frontend, B08 и PRS1 не изменены. Только synthetic data.

## Результат и контракт

H03 устранён для **отдельного доверенного внутреннего provisioning LOGIN**. Существующий `PostgresIdentityMapping(..., restrictedCapabilities: true).CreateInactive` вызывает одну закрытую SQL capability вместо EF INSERT. Application runtime по-прежнему не может создавать identity. Это намеренное разделение полномочий, а не failed provisioning под migration owner. Успешное создание проверяется под `session_user=c2j_provisioning`; S2/S3/issuance — под `c2h_runtime`.

Actor → use case → invariants → entities → transitions → authority/data sources → tests:

- Доверенный внутренний provisioning caller → CreateInactive → уникальные UserId, ActorId и normalized username; explicit stable mapping; passwordless inactive identity + audit атомарны → IdentityUser, Actor, ActorIdentity, AccountSecurityState, SecurityAudit → absent → inactive/Enabled epoch=version=1 → отдельный DB credential является административной capability; IDs обозначают запись, не доказывают право → success, duplicate/races, rollback, lost ACK, no implicit grants и runtime denial.
- Внутренний S2 adapter → прежние Authenticate/Rotate/Revoke/RevokeAll/Issue → прежние credential, security, session и temporal checks → те же identity/session rows → прежние переходы → закрытая блокировка аккаунта вместо column UPDATE grants; rehash только отдельной capability после проверки пароля CLR adapter → restricted S2/issuer suite и rehash rollback.
- Внутренний S3 adapter/reader → membership/enrollment/teacher grants и fresh whole-course evidence → org/course/group/generation boundaries → существующие S3 tables/functions → grant/move/revoke/regrant → существующие SQL capability и server-resolved reader → restricted positive/negative matrix. Whole-course predicate существует только в тесте и не является production import authorization adapter.

Caller authority намеренно не выводится из `OrganizationId`, `ActorId`, normalized name или role flags. Credential provisioning — доверенная deployment-assigned capability для внутреннего synthetic manifest; его нельзя передавать пользовательскому HTTP host или считать scoped org-admin authentication. Привязка человека-инициатора, проверка manifest/представителя и будущий org-admin ingress не реализованы этим slice. Произвольный новый Actor не является механизмом обнаружения реального человека-дубля. Existing actor association допускается только как доверенная explicit association, уже mapped actor отвергается uniqueness constraint. Membership, enrollment, teacher grants, session, activation/recovery challenge и JWT не создаются provisioning-функцией.

## Paths и SQL capabilities

| Path | Изменение C2-J |
|---|---|
| `src/Lms.Infrastructure/PostgresIdentityMapping.cs` | Opt-in путь вызывает `s2_create_inactive`; default EF path сохранён; отдельной provisioning системы/API нет |
| `src/Lms.Infrastructure/PostgresIdentitySessions.cs` | Opt-in `LockAccount` через закрытую функцию; opt-in rehash через CAS capability; existing C2-I audit path сохранён |
| `tools/processing_isolated/c2j-capabilities.sql` | Новый isolated candidate после C2-H/C2-I; production migration отсутствует |
| `tests/Lms.Api.Tests/IsolatedProcessingDeploymentTests.cs` | Отдельный LOGIN и candidate install; новый allowlist runtime=5 функций |
| `tests/Lms.Api.Tests/IsolatedIdentityCompatibilityTests.cs` | IdentitySeed создаётся ограниченным provisioning LOGIN; прежний raw identity UPDATE теперь должен отказать; owner только seed activation/fault/lock observation |
| `tests/Lms.Api.Tests/IsolatedIdentityProvisioningTests.cs` | Provisioning, DML/DDL/role denial, concurrency, rollback, ACK loss, rehash |
| `tests/Lms.Api.Tests/IsolatedScopedAuthorityMatrixTests.cs` | Restricted S3 positive/negative matrix |

Все новые functions: owner `c2h_owner`, `SECURITY DEFINER`, `SET search_path=pg_catalog`, schema-qualified tables, typed parameters, без dynamic SQL, DDL и собственного commit.

- `s2_create_inactive(uuid,uuid,uuid,text,text,timestamptz)`: nonempty UUID/name, length≤256, finite timestamp; существующий .NET `UpperInvariantLookupNormalizer` передаёт normalized name, включая Unicode, без замены SQL locale-dependent upper. Raw capability является доверенным внутренним интерфейсом, а не валидацией публичного произвольного normalized-name payload. Создаёт отсутствующий Actor через INSERT ON CONFLICT DO NOTHING; затем IdentityUser без password/email/phone и с server-generated stamps, inactive mapping, Enabled security state epoch/version 1; вызывает owner-only `s2_mapping_audit`. Uniqueness/FK/check constraints сохранены. Existing actor не обновляется, mapping не перепривязывается.
- `s2_lock_account(uuid)`: блокирует AccountSecurityState → AspNetUsers → ActorIdentities тем же FOR UPDATE, затем возвращает security row. Adapter читает user/mapping под удержанными locks. Не изменяет строки и не принимает SQL/table names.
- `s2_rehash_password(uuid,text,text)`: тот же lock order; только существующая Enabled/active identity, nonnull expected hash, bounded replacement; UPDATE PasswordHash с expected-hash CAS, mismatch→40001. Не активирует identity, не меняет epoch, mapping, grants или sessions. Проверка plaintext password остаётся в существующем CLR adapter. SQL capability сама не доказывает знание plaintext: ранее runtime уже имел прямой UPDATE PasswordHash и SELECT hash. Это сужение существующей доверенной S2 возможности, не новый authentication boundary против компрометации runtime credential.

## Grants

| Principal | Итоговая application capability |
|---|---|
| `postgres` bootstrap | Только disposable database/roles и existing extension ACL; не исполняет проверяемые application paths |
| `c2h_owner` | Non-superuser migration/schema/fixture owner; только disposable setup, activation seed, fault injection и persisted-state observation |
| `c2j_provisioning` | LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS; CONNECT + USAGE public + **только EXECUTE s2_create_inactive**; нет table/sequence DML, SELECT или S2/S3/processing EXECUTE |
| `c2h_runtime` | Existing C2-H/C2-I allowlist, **удалены** UPDATE(UserId) AccountSecurityState/ActorIdentities и UPDATE(PasswordHash) AspNetUsers; **добавлены** EXECUTE s2_lock_account/s2_rehash_password; итого 5 public functions с S3, session audit и issuing org lock |
| `c2h_processing` | Existing processing_change / processing_find_command; 2 функции, новых прав нет |
| PUBLIC | Новые signatures REVOKE ALL; default deny сохранён |

Runtime не получает INSERT/UPDATE/DELETE, включая column INSERT/UPDATE, на Actors/AspNetUsers/ActorIdentities/AccountSecurityState. Provisioning LOGIN не может SET ROLE, CREATE/TEMP, заменить функцию, изменить identity/audit/session tables напрямую, вызвать S3 или rehash. Обе processing composition helpers закрыты для всех трёх operational LOGIN. Session/verifier DML runtime — принятая existing S2 capability, не provisioning grant. Bootstrap/owner credentials не добавлены в production DI/compose или adapter fallback. Profile применяется только в disposable/quiesced setup; production secret delivery не реализована.

## Транзакции, concurrency и неизвестный исход

CreateInactive использует fresh dedicated DbContext, BeginTransaction ReadCommitted, один capability call, один Commit. Порядок вставок Actor → User → Mapping → Security → Audit; FK и unique constraints решают конфликтующие создания. Existing actor не требует reassignment. ON CONFLICT относится только к идентичному ActorId; user/mapping/name conflicts не поглощаются. Код не ловит error для продолжения, не открывает отдельную connection и не повторяет команду автоматически. Audit failure откатывает и новый Actor. При уже существующем Actor откат не удаляет его.

Семантика повтора сохранена: stable user/actor/name duplicate →23505; ровно одна identity и один mapping audit. Нет нового command-id/idempotency receipt contract. Три контролируемые гонки блокируют участников до одновременного ожидания PostgreSQL locks: одинаковый запрос, разные users с одним Actor, разные Actors с одинаковым normalized name. После release — один success и один 23505. Lost COMMIT ACK: proxy подавляет реальный server COMMIT response; adapter бросает exception и не возвращает IdentityLink, но все строки/audit committed; явный duplicate retry конфликтует, проверочное чтение через runtime `Find` возвращает исходную связь. Нет вывода «exception означает rollback».

S2 сохраняет security→user→mapping→session→issuing organization; RevokeAll сортирует session IDs. Rehash в той же транзакции session/verifier/audit; before-commit failure откатывает также hash. Existing C2-I disabling EF auto-savepoints применяется только к S2 save path для xmin audit source proof и восстанавливает настройку в finally. Provisioning функция пишет все rows напрямую в owning transaction и не нуждается в EF SaveChanges/savepoint workaround. Default paths не переключены.

## Фактическая matrix и regression

Финальный полный прогон:

```sh
cd /home/tea/github/lms
/tmp/b07c2g/sdk/dotnet test Lms.sln -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/b07c2j/regression
```

**PASS 732/732, failed 0, skipped 0**: Domain 43, Application 272, Infrastructure 198, API 219. В API — **33 isolated LOGIN tests** (21 существующий C2-H/C2-I и 12 C2-J cases). Новые negative H04 tests подтверждают denial, не Ready success. Каждый isolated test запускает отдельный disposable PostgreSQL 17.6 container и полный migration chain. Успешные C2-J/S2/S3 операции выполняются отдельными LOGIN credentials без SET ROLE подмены.

Evidence: [regression.log](evidence/MVP-B07b-C2-J/regression.log), [test-summary.json](evidence/MVP-B07b-C2-J/test-summary.json), четыре TRX рядом. Предварительный targeted run: `/tmp/b07c2g/sdk/dotnet test tests/Lms.Api.Tests/Lms.Api.Tests.csproj -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~IsolatedProcessingDeploymentTests --logger trx --results-directory /tmp/b07c2j/targeted` — 31/31 PASS до добавления двух S3 tests; не суммируется с финальным прогоном. Финальный прогон также содержит уточнённую независимую гонку normalized name с разными ActorId.

| Обязательство | Проверка / граница evidence |
|---|---|
| CreateInactive restricted success | Реальный provisioning LOGIN; четыре rows + inactive audit, Unicode normalizer; новый и existing unmapped Actor |
| No implicit capabilities | Нет session/verifier/membership/enrollment/teacher grants; inactive Authenticate denied, JWT issue denied; runtime/processing не исполняют provisioning |
| Duplicate / concurrency | User/Actor/normalized-name conflicts→23505; три overlapping schedules, ровно один winner и один audit |
| Identity + audit atomicity | Audit trigger P0001 и BeforeCommitFailure → нет ни одной из четырёх новых rows/audit; следующий create succeeds |
| Lost COMMIT ACK | Provisioning committed, no returned result; explicit retry conflict; Find reconciles. Existing C2-I session/issuer wire faults повторяются |
| Authenticate/Rotate/Revoke/RevokeAll | Existing restricted LOGIN S2 success/audit/expiry/replay tests, теперь identity seed через provisioning LOGIN |
| Rehash | Legacy IdentityV2 hash → successful restricted login/rehash; precommit failure сохраняет старый hash и не создаёт session/audit |
| S2 faults/locks | Existing real 40001/40P01 session/audit rollback; issuer удерживает security lock до Organization; owner NOWAIT только наблюдает lock, не подменяет issuing caller |
| S3 membership/whole course | Teacher + matching active whole-course grant permits; Student, group-only teacher, administrative flags without Teacher scope, unknown actor, wrong org/course/group denied |
| S3 revoke/move/generations | Teacher grant revoke/regrant, membership revoke/regrant, stale teacher generation; enrollment move/revoke/regrant, stale version/membership generation; old grants не оживают |
| Issuance | Restricted Student и whole-course Teacher issuance; membership revoke запрещает новую выдачу; ранее выпущенный JWT валиден по принятым delayed semantics |
| Protected DML/DDL/SET ROLE | Три operational LOGIN; direct identity INSERT/UPDATE/DELETE/TRUNCATE/ALTER denied, has_any_column_privilege false; owner/bootstrap/peer SET ROLE denied; provisioning S3/rehash/audit/session denied |
| ProcessingGuard | Preflight всех трёх principals после grants; existing dangerous-audit-grant negative test и repeat profile install |
| H04 helpers | processing_begin_write/assert_current →42501 у runtime/processing/provisioning; Ready не заявляется |
| Applicable .NET regression | Полная solution suite; standard fixture results не выдаются за restricted LOGIN evidence |
| Browser/compiler/import/storage | NOT RUN — paths не изменены; никаких production/browser E2E claims |

## Оставшиеся несовместимые operational writers

- Default (`restrictedCapabilities=false`) IdentityMapping, IdentitySessions и ComponentTokenIssuer несовместимы с этим restricted profile: EF INSERT/audit и direct FOR UPDATE требуют прежних прав. Production host не переключён. Opt-in должен быть явным, без fallback к owner.
- Generic EF/raw SQL записи organization/context/binding/restriction/identity/audit, включая privileged fixture seed и LocalDevelopment, не являются разрешёнными operational writers. Processing changes требуют existing `PostgresProcessingStateAdministration` с отдельным executor; provisioning organization/course/context не включено в C2-J.
- Activation/recovery/security-block domain contracts и существующие тесты не изменены. В текущем Infrastructure нет отдельного production activation/recovery adapter. Privileged test activation seed не является доказательством restricted operational activation/recovery. Их будущие writers обязаны сохранить ADR-L17 lock/epoch/session/audit/independent approval semantics; rehash не заменяет recovery.
- `PostgresProcessingWriteBoundary` требует private composition helpers. Его callback не превращён в универсальный service и не открыт runtime.
- Migration/emergency owner технически может обходить ACL/guards: только отдельный controlled/quiesced runbook. Existing compose/bootstrap credential separation не перенесено в production.

## H04 и условия controlled DB runtime cutover

**H04 DEFERRED TO IMPORT WRITER; production ImportAttempt Ready BLOCKED.** Будущий import writer требует отдельно принятой закрытой capability, которая в одной owning transaction связывает immutable import owner/org/course/context, fresh S2/S3 whole-course authority, ProcessingGuard revision/current restrictions, state/CAS/transition, journal/audit и commit. Нужны narrow executor ACL, bounds/cancellation, идемпотентность/reconciliation неизвестного исхода и restricted Ready-first/restriction-first concurrency evidence. Одного EXECUTE на private helpers недостаточно; C2-J их не выдаёт и не реализует production Ready.

Для controlled cutover необходимы: owner acceptance C2-J; отдельное разрешение deployment/cutover; полный inventory live principals/writers и secret delivery с физическим разделением owner/runtime/provisioning/processing; явный выбор opt-in adapters; проверка exact ACL/default/extension ownership и preflight в целевом изолированном deployment; отсутствие fallback/direct incompatible writers; отдельное решение по activation/recovery/organization provisioning (реализация либо явно quiesced scope); отдельный import writer/H04 контракт до включения Ready. Пройденный isolated S2/S3 matrix не разрешает общий server/HTTP cutover.

## Guardrails §7 и preservation

| Check | Статус |
|---|---|
| Actor/use case/invariants/states | covered — контракт выше |
| Accepted S2/S3/PRS1/gates | covered — contracts сохранены; PRS1 exact OFF; H04 отдельно blocked |
| Org/course/group/ownership, client authority/IDOR | covered — credential separation и explicit mapping; S3 negatives; user-facing admin authority не реализована и не заявляется |
| Subjects/embedded sources/current processing | N/A для новой identity creation: нет учебного resource/context; processing writes/Ready не добавлены; existing regression |
| Lists/counts/pagination leakage | N/A — capability не возвращает lists/counts; existing suite повторена |
| Attachments/file-parent/history | N/A для изменённых paths; mapping не перепривязывает history, существующие тесты повторены |
| Transactions/CAS/retry/unknown outcome | covered — atomicity, races, rehash CAS, wire ACK evidence |
| Validation/limits/safe errors | covered — existing CLR validation, typed SQL bounds/finite time, unique/FK constraints; trusted normalization contract явно указан |
| Unit/integration/negative/E2E | exact results ниже; TestServer synthetic workflow из existing suite не означает browser/HTTP acceptance |
| Scope/schema/security | isolated SQL + existing adapters; без production migration, нового API или changes to PRS1 |
| Real data/secrets/logs | synthetic only; random LOGIN credentials только в памяти fixture; secrets не записываются в evidence |
| Evidence/readiness | C2-J OWNER ACCEPTANCE PENDING; CUTOVER BLOCKED, не PILOT/E2E ACCEPTED |

Skills model-domain/implement-use-case/implement-feature отсутствуют в доступном каталоге; не выполнялись. Прочитаны AGENTS, guardrails/reconciliation, C2-I, применимые ADR-L01/L02/L15/L17/L18 и S3 contracts. Предсуществующая dirty branch сохранена согласно прямому owner запрету смены branch/HEAD/index и AGENTS. Branch `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` и index сохранены; commit/push/merge/rebase/deploy не выполнялись. [preservation.json](evidence/MVP-B07b-C2-J/preservation.json) сравнивает все исходные tracked/untracked files; предсуществующая whitespace-ошибка `docs/content/COURSE-CONTENT-SPEC.md:1325` не исправлялась.

**C2-I ACCEPTED · C2-J OWNER ACCEPTANCE PENDING · H04/READY BLOCKED · SERVER/HTTP CUTOVER NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
