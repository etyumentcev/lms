# ADR-L01 — ASP.NET Core Identity integration

2026-10-09. **ACCEPTED D3/T01/T03/T04** в границах [owner register](../../codex/L03-OWNER-DECISION-REGISTER.md). Реализация NOT VERIFIED. Прежняя рекомендация Identity cookie-only с per-request session DB check отменена окончательным T04; [ADR-L15](ADR-L15-jwt-delayed-revocation.md) — единственная нормативная семантика ordinary revoke.

## Существующая граница

[Program](../../src/Lms.Api/Program.cs) уже использует JwtBearer; [ActorMapper](../../src/Lms.Api/ActorMapper.cs) ожидает authenticated principal, UUID sub и Student/Teacher. Identity issuance/refresh и новая scope schema этим checkpoint не реализованы. Development token tooling не является production login и не доказывает новый 15-минутный предел. Domain/Application не должны зависеть от Identity entities.

## Решение

ASP.NET Core Identity хранит credentials; пароли 12–128 символов. AspNetUsers и стандартные связанные Identity tables дополняются Actors/ActorIdentities. IdentityUser соответствует одному стабильному Actor UUID; UUID credentials не подставляется в historical ActorId. Один actor может иметь несколько OrganizationMemberships; основная Student/Teacher role не заменяет organization/course/group scope. Administrative permissions не дают automatic work access. Существующая history и IDs сохраняются; cascade от Identity/Organization запрещён.

Нужны структуры provisioning, single-use challenges, sessions/refresh lifecycle, AccountSecurityState и SecurityAudit. Их наличие не возвращает обязательную серверную cookie-сессию. Поля/indexes/context placement и точные auth DTO — инженерный дизайн. Account/mapping/audit атомарны; membership/enrollment/teacher grants — отдельные use cases. Grants старого Generation после regrant не оживают.

Access JWT подписан, lifetime ≤15 минут; local signature/issuer/audience/lifetime/scope validation. Current DB authority при выдаче/refresh, scoped deadline и duration bounds по ADR-L15. Нет обязательной DB проверки актуальности grants на каждом HTTP. Public MapIdentityApi/register не открывается.

Browser storage/refresh/logout/reset — **OPEN E02**. PROPOSED access в памяти + refresh Secure HttpOnly host-only cookie; CSRF там, где cookies служат credential. Не сохранять секреты в URL/logs/localStorage. Не переносить прежние session idle/absolute defaults в accepted policy. Logout очищает local state; немедленный отзыв всех старых access JWT не обещан. Emergency — E03.

Frontend на account/context loss aborts requests, очищает drafts/Blob URLs/cache и remounts; cross-tab signals без секретов. Expiry требует нового входа/refresh по согласованному contract, без автоматического replay Submit/Review/Upload. Шесть основных экранов и business DTO сохраняются; минимальные auth формы и org-admin UI T02 добавляются отдельно.

## Проверка и ограничения

Additive M1–M3 по T06; mapping только explicit synthetic manifest, unknown quarantine, no automatic enrollment. Tests: mapping uniqueness/malformed principal, forged claims, signatures/issuer/audience/expiry, code consume races/rate limits/NAT, multi-org scope/refresh, generations, CSRF, cross-tab account switch, hashes/history unchanged. Тесты L1 NOT RUN; [plan](../../codex/L03-L1-IMPLEMENTATION-PLAN.md) требует отдельного разрешения. Consent/legal basis/representative/erasure workflows вне L1.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY. LEGAL SOURCE NOT VERIFIED.
