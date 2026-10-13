# MVP-B07b-C2-I — isolated S2 / issuer compatibility

2026-10-11. **ISOLATED IMPLEMENTATION · ADDITIONAL PRIVILEGE BLOCKERS · OWNER ACCEPTANCE PENDING.**

Основание: явное OWNER AUTHORIZATION C2-I в текущей задаче; C2-H принят с H01/H02. Общий runtime cutover не разрешён и не выполнен. Канонический checkout `/home/tea/github/lms`; существующая dirty branch сохранена.

## Результат и пределы

H01 session paths переведены на закрытую audit capability в opt-in isolated режиме. H02 issuer получает тот же Organization `FOR UPDATE` без UPDATE privilege. Identity mapping audit также имеет закрытый adapter path, но **успешный CreateInactive под runtime LOGIN заблокирован H03**: C2-H не разрешает необходимые INSERT в identity tables. Полная Ready concurrency matrix на неизменённом C2-H LOGIN profile **заблокирована H04**: отсутствуют EXECUTE composition helpers. Ни один дополнительный table/helper privilege для обхода этих blockers не выдан.

Это не утверждение, что вся обязательная restricted matrix завершена. Общий cutover остаётся BLOCKED. Проверки отсутствующих privileges — доказательство blocker, а не successful implementation этого path.

## Контракт

Actor → Use Case → Invariants → Entities → Transitions → Authorization/data sources → Tests:

- Внутренний S2 adapter → Authenticate/Rotate/Revoke/RevokeAll → прежние credential/account/session checks, атомарность session + verifier + audit → IdentitySession/RefreshVerifier/SecurityAudit → create/rotate/retire → source rows в собственной PostgreSQL transaction; никаких клиентских actor/action payloads для административного аудита → restricted success, forgery denial, rollback/fault tests.
- Внутренний mapping adapter → CreateInactive → стабильная explicit User↔Actor association, inactive identity без grants → IdentityUser/ActorIdentity/AccountSecurityState → создание + audit → actor/org выводятся из новой mapping row → owner positive control и runtime denied/no-partial-effect; provisioning boundary H03.
- Component issuer → Issue → S2 checks, S3 current authority и неизменный JWT temporal contract → session/organization/membership → read/sign/commit → S2 locks затем Organization FOR UPDATE → restricted issuance, membership revoke, contention/order, lost ACK.

S2 сохраняет существующий ReadCommitted transaction owner: fresh dedicated DbContext и один BeginTransaction/Commit. AccountSecurityState → AspNetUsers → ActorIdentities → session (RevokeAll — сортировка SessionId), затем Organization только в issuer. CAS/version/generation logic, refresh consume-before-successor, replay window и Now-after-lock не изменены. Capability не открывает отдельную transaction и не делает commit. Ошибки выходят наружу, await-using откатывает незавершённую transaction; автоматического retry/replay нет. Unknown COMMIT outcome не трактуется как rollback; результат/secret/JWT не возвращается после commit exception.

SQL source proof использует `xmin = pg_current_xact_id()::xid`. Для isolated SaveChanges временно выключены автоматические EF savepoints, прежняя настройка восстанавливается в finally. Иначе EF subtransaction xmin не равен top-level XID. Это не частичный recovery: S2 не ловит SaveChanges errors для продолжения, вся операция завершается ошибкой и откатывается. Ambient/nested transaction composition не добавлена. Production default сохраняет прежние EF savepoints.

## Изменённые paths

| Path | Изменение |
|---|---|
| `PostgresIdentitySessions.Authenticate` | `session_created`: SaveChanges session/verifier, затем SQL audit, один commit |
| `Rotate` success | consume predecessor сохраняется до successor; `session_rotated` после сохранения нового generation |
| `Rotate` expired/blocked/backward-clock | `session_unavailable`, прежний Retire и RevokedAt clamp |
| `Rotate` late replay | `refresh_replay`; concurrent replay window не пишет новый audit |
| `Revoke` | `session_revoked`, no-op semantics сохраняются |
| `RevokeAll` | `sessions_revoked` для каждой ранее live session; прежний порядок locks |
| `PostgresIdentityMapping.CreateInactive` | `identity_mapping_created` из persisted inactive mapping, в её transaction; runtime provisioning BLOCKED H03 |
| `PostgresComponentTokenIssuer.Issue` | query закрытой lock function вместо прямого FOR UPDATE при opt-in; S3 reader/sign/commit/post-commit validate неизменны |

Три adapters имеют optional `restrictedCapabilities=false`; только isolated tests передают true. Это явный выбор host, не автоматический fallback после 42501. Production DI, compose, credentials, HTTP, JWT implementation, ProcessingGuard и existing migrations не изменены.

## SQL capabilities

Candidate: [c2i-capabilities.sql](../tools/processing_isolated/c2i-capabilities.sql). Применяется только isolated launcher после существующего [profile.sql](../tools/processing_isolated/profile.sql), после полного migration chain. Повторное применение candidate предусмотрено CREATE OR REPLACE и проверено повторным profile test. Production migration не добавлена.

Все три functions: `SECURITY DEFINER`, owner `c2h_owner`, `SET search_path=pg_catalog`, schema-qualified application tables, никаких dynamic SQL/DDL/commits. PUBLIC и processing EXECUTE отозваны; runtime получает только session-audit и organization-lock signatures. Mapping-audit остаётся owner-only до отдельного provisioning contract.

- `s2_session_audit(uuid,text,timestamptz)`: ровно 6 session actions из таблицы выше; action ≤32 bytes; non-null finite timestamp. Источник — session version текущей top-level transaction. Проверяются generation/live/revoked state, reason и соответствующий timestamp. Subject/resource/generation выводятся из session. AuditId/TraceId генерирует сервер. Result фиксирован `success`; ActorId/OrganizationId/context/evidence всегда NULL, как в существующем S2. UUID references и trace имеют фиксированный размер; произвольные строки/JSON/actor/admin action не принимаются.
- `s2_mapping_audit(uuid,timestamptz)`: source — inactive mapping + passwordless user + Enabled security state epoch/version 1, все записаны текущей transaction. Action/result фиксированы `identity_mapping_created`/`inactive`; ActorId/OrganizationId выводятся из mapping. Нет actor/org/action parameters. Runtime не может создать эти source rows при текущем profile и не имеет EXECUTE этой функции. xmin сам по себе не различает INSERT и UPDATE. Дополнительно проверяются initial security version/epoch; существующий monotonic-security trigger запрещает no-op security update. Независимо от этих checks, отдельный negative test обновляет разрешённые user/mapping columns и подтверждает отказ runtime EXECUTE. Необходимость runtime mapping-audit grant отсутствует до решения H03.
- `s2_lock_issuing_organization(uuid)`: исходный SELECT Organization FOR UPDATE, включая lock существующей недопустимой строки; возвращает только Enabled + SyntheticOnly. Missing/unknown/empty/null ID или недопустимая organization дают пустой результат, issuer deny. Row lock удерживается до конца caller transaction. Не пишет organization/ownership/processing rows, не вызывает S2 locks и не использует advisory locks.

Scope этой защиты: невозможно через новые audit signatures записать произвольное administrative/processing action, actor, context или evidence. Это не новый authentication boundary для скомпрометированного runtime: существующий C2-H разрешает direct session DML, и capability не доказывает, что пароль был проверен CLR adapter. Также это не универсальная система exactly-once audit для raw SQL callers. No automatic replay сохранён.

## DB grants

| Principal | C2-I grants / итог |
|---|---|
| bootstrap `postgres` | Только disposable roles/database и существующий extension ACL lockdown; application/executor operations не выполняет |
| `c2h_owner` LOGIN | Existing non-superuser migration/schema/fixture owner; владеет новыми functions; provisioning synthetic data отдельно от runtime |
| `c2h_runtime` LOGIN | Весь table/column allowlist C2-H без изменений; **+ EXECUTE только session-audit и organization-lock signatures**; вместе с S3 ровно 3 callable public functions |
| `c2h_processing` LOGIN | Только прежние `processing_change` и `processing_find_command`; ровно 2 callable public functions; ни одной новой capability |
| PUBLIC | Новые functions недоступны; прежние schema/default deny/extension ACL сохранены |

Runtime SELECT/INSERT/UPDATE allowlist полностью остаётся в [profile.sql](../tools/processing_isolated/profile.sql) и C2-H grants matrix. SecurityAudit: direct INSERT/UPDATE/DELETE отсутствуют. Organizations: direct INSERT/UPDATE/DELETE и любой column UPDATE отсутствуют. Identity provisioning INSERT не добавлен. Processing helpers не открыты. Нет role memberships, SET ROLE к owner, CREATE/TEMP/DDL, новых educational roles/claims/user-facing permissions. Preflight обеих operational roles проходит после candidate; диагностический GRANT INSERT SecurityAudit по-прежнему отвергается preflight и откатывается.

## Фактические проверки

Финальная команда:

```sh
cd /home/tea/github/lms
/tmp/b07c2g/sdk/dotnet test Lms.sln -m:1 -p:UseSharedCompilation=false --logger trx --results-directory /tmp/b07c2i/final-verified
```

**PASS 720/720, failed 0, skipped 0:** Domain 43, Application 272, Infrastructure 198, API 207. В API — 21 isolated test (5 существующих C2-H и 16 новых C2-I); реальные отдельные LOGIN principals, без SET ROLE подмены. Эти 21 включают negative tests H03/H04: зелёный результат не снимает blockers.

Evidence: [regression.log](evidence/MVP-B07b-C2-I/regression.log), [test-summary.json](evidence/MVP-B07b-C2-I/test-summary.json), TRX в том же directory. Промежуточные диагностические прогоны не суммируются. Исправлены source-proof несовместимость с EF savepoints, test fixture без enrollment, single-connection wire proxy lifetime и наблюдение pg_stat_activity от другого non-superuser. Один промежуточный run остановлен из-за fixture setup, пытавшегося seed enrollment после удержания org lock; порядок исправлен.

| Обязательство | Evidence / предел |
|---|---|
| Authenticate/Rotate/Revoke/RevokeAll | Новый restricted LOGIN test проверяет success, persisted audit counts/fields и revoked sessions |
| Все session audit branches | Отдельно concurrent replay без retire, late replay, idle expiry; существующая полная S2 suite — default adapter |
| Mapping/audit | Owner positive control использует новую capability, runtime CreateInactive →42501 и отсутствие partial rows; runtime Find/read не требует новых прав. Successful restricted creation **BLOCKED H03** |
| Issuance + S3 | Реальный runtime membership/enrollment grant, issue, missing/empty org deny, membership revoke, current authority deny, старый JWT продолжает валидироваться по accepted delayed semantics |
| Полная S3 regression | Existing standard fixture suite в solution run; это не полный новый LOGIN S3 matrix. Новый LOGIN test покрывает membership/enrollment/issuer paths выше |
| Session + audit atomicity | Audit INSERT trigger failure откатывает session/verifier; pre-commit exception откатывает session и уже выполненный SQL audit; success сохраняет обе части |
| Forged audit | Administrative/processing/oversize actions отвергаются также при source row, изменённой текущей transaction; old transaction source/mapping отвергаются; overload с произвольным actor отсутствует (42883); остальные denial →42501 |
| Org lock contention/order | Runtime function блокирует существующий S3 writer (55P03), освобождается после rollback; issuer при ожидании Organization уже держит AccountSecurityState (NOWAIT→55P03); после release issue успешен |
| Rollback/40001/40P01 | Реальные restricted LOGIN PostgreSQL transactions: serialization conflict после session/audit write и deliberate reversed session-lock deadlock; никакого audit/session partial commit. Fault schedules используют raw SQL для контролируемого injection; не заявляется новый adapter retry protocol |
| Lost COMMIT ACK | Реальный wire relay на runtime LOGIN: Authenticate commit сохраняет session/verifier/audit, secret не возвращается; issuer JWT не возвращает. Single connection удерживается через Authenticate discovery |
| Direct protected DML | Обе operational roles: INSERT/UPDATE/DELETE SecurityAudit, UPDATE/DELETE Organizations →42501; прежняя полная C2-H DML/DDL/ownership/defaults matrix повторяется |
| ProcessingGuard preflight | После capability install и после S2 writes; existing dangerous-grant test и весь C2-G regression повторяются |
| Restriction-first / Ready-first | Existing C2-G regression в полном solution run; использует ограниченные SET ROLE fixtures. На неизменённых C2-H LOGIN credentials **BLOCKED H04**, не выдаётся за PASS |
| Synthetic Submit→Review→Revise→Accept | Existing TestServer application с runtime LOGIN, owner только seed; foreign student denied. Не browser/edge/S4 HTTP acceptance |
| .NET | Финальный solution run выше; browser/Python/compiler/storage probes NOT RUN, соответствующие paths не менялись |

## Дополнительные blockers и оценка безопасности

**H03 — closed identity provisioning отсутствует.** `PostgresIdentityMapping.CreateInactive` вызывает EF INSERT в `Actors` (если actor новый), `AspNetUsers`, `ActorIdentities`, `AccountSecurityState`, до audit. Profile не имеет этих INSERT; реальный runtime test получает 42501 и полный rollback. Одна audit capability это не исправляет. Прямые INSERT позволили бы runtime создавать credential/mapping/security rows и расширили бы security boundary. Нужен отдельно принятый closed provisioning path/principal и его authority contract либо явное deployment-only/quiesced provisioning решение. Здесь нет автоматического GRANT, privileged fallback или подмены restricted success owner fixture.

**H04 — Ready composition не является capability C2-H.** `PostgresProcessingWriteBoundary.Execute` вызывает `processing_begin_write(uuid,bigint)` и `processing_assert_current(uuid,text,uuid,bigint,timestamptz,uuid[])`. Runtime и executor имеют отказ 42501 на обе функции. C2-G Ready probe дополнительно имеет test-only function/tables и отдельный writer role; это не существующая C2-H application capability. Автоматическое открытие private helpers нарушило бы ограниченный allowlist и само по себе не сделало callback SQL безопасным. Нужен отдельно согласованный isolated writer/test principal contract или закрытая composed capability. Server Ready не включён; granting helpers/универсальный writer не реализованы.

Полный новый LOGIN S3 negative-access matrix сверх membership/enrollment/issuer checks — **NOT PROVEN**. Existing standard full S3 regression не заменяет его. Это остаётся условием cutover, даже при зелёном solution run.

## Cutover: подтверждено / не доказано

Подтверждено для isolated profile: отдельные реальные logins; узкие capabilities без прямого protected DML; session audit atomicity; issuer row-lock level/order; default production adapters не переключены; ordinary JWT delayed revocation и no-replay unknown outcome сохранены в проверенных paths.

Не доказано/заблокировано: successful restricted identity provisioning; Ready composition/concurrency на C2-H LOGIN; полный restricted S3 matrix; общий writer cutover и production secret delivery; production ImportAttempt/Ready; G02 bounded execution, real-data/legal gates, HTTP rollout. Для cutover требуется закрыть H03/H04 и оставшиеся proof obligations, затем отдельное owner authorization. Новые grants в этой задаче ограничены двумя согласованными runtime H01/H02 capabilities; mapping-audit остаётся owner-only.

## Preservation и guardrails §7

Изменены только три adapter source files, existing isolated launcher (partial class, candidate load, 3-function allowlist), добавлены candidate SQL, isolated compatibility tests/wire injector и этот report/evidence. [preservation.json](evidence/MVP-B07b-C2-I/preservation.json) сверяет все предсуществующие tracked/untracked files, branch, HEAD, index. `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39` сохранены; dirty checkout не переключался на main. Commit/merge/rebase/push/deploy не выполнялись.

| §7 check | Статус |
|---|---|
| Actor/use case/invariants/transitions | covered — контракт и path matrix выше |
| S2/S3/PRS1 и gates | covered для изменённых paths; полный restricted matrix ограничен явно указанными blockers; exact OFF |
| Org/course/group/ownership и client authority/IDOR | covered existing regression и restricted issuance/membership; новых ingress selectors нет; полный LOGIN matrix NOT PROVEN |
| Subjects/embedded processing evidence | existing ProcessingGuard/reader regression; capabilities не меняют processing applicability; Ready LOGIN BLOCKED |
| Lists/counts/pagination leakage | N/A для новых capabilities: нет lists/aggregates; existing regression повторена |
| Attachments/file-parent/history | N/A для изменённых paths: не затронуты; existing regression повторена |
| Transaction/CAS/retries/unknown outcome | covered tests выше; fresh context и no replay, savepoint detail явно документирован |
| Validation/limits/errors | covered finite typed values, action allowlist, source/state proof, fixed fields и denial tests |
| Unit/integration/negative/E2E | точный final run/evidence выше; restricted gaps не маркируются PASS; TestServer не production E2E |
| Scope/schema/security | isolated candidate only, два узких runtime EXECUTE grants; additional privileges остановлены H03/H04 |
| Real data/secrets/logs | synthetic-only; random credentials только в памяти fixture, не в artifacts; никаких real data |
| Evidence/readiness | OWNER ACCEPTANCE PENDING, CUTOVER BLOCKED; отсутствующие обязательства перечислены |

Skills `model-domain`, `implement-use-case`, `implement-feature` отсутствуют в доступном каталоге; их исполнение не заявляется. Прочитаны AGENTS.md, guardrails/reconciliation, C2-H, S2 results, применимые ADR-L15/L18. Принятые temporal semantics не переопределены.

**C2-H ACCEPTED · C2-I OWNER ACCEPTANCE PENDING WITH H03/H04 · READY BLOCKED · SERVER/HTTP CUTOVER NOT AUTHORIZED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
