# ADR-L02 — Closed account provisioning and recovery

2026-10-09. **ACCEPTED C01/C02 — бизнес-процесс**, owner: владелец, evidence: L0.3 §3. Параметры/интерфейс **PROPOSED T01/T02**. [Реестр](../../codex/L-DECISION-REGISTER.md), [план](../../codex/L03-L1-IMPLEMENTATION-PLAN.md).

Учётные записи по списку учеников/преподавателей создаёт уполномоченный администратор образовательной организации. Обязательных email/телефона и публичной регистрации нет. Активация — одноразовый код, переданный лично или по предварительно проверенному индивидуальному каналу; выдача не раскрывает других пользователей. Recovery — новый одноразовый код после проверки личности/полномочий, отзыв существующих сессий по выбранной security policy, без новых учебных прав.

Это supersedes L0 operator-only issuer и объединение provision account с enrollment. Platform Management не даёт ни выдачи accounts от имени любой организации, ни чтения работ. Организационный администратор имеет отдельные scoped administrative permissions; Admin не добавляется в Domain.AuthorRole. Первого администратора организации назначают через отдельный approved bootstrap manifest, а не через self-grant.

Фактическая база: [LocalDevelopment](../../src/Lms.Api/LocalDevelopment.cs) — demo seed, [AccessFacts](../../src/Lms.Infrastructure/AccessFacts.cs) — enrollment и teacher grants, account persistence отсутствует. Учебный grant и Identity credential — разные изменения. Новый account не создаёт membership/enrollment/assignment автоматически: каждый отдельный use case авторизуется и аудируется.

Предлагается ограниченная административная web-форма для уполномоченного администратора без SSH/DB доступа; альтернатива — CLI-клиент тех же API под его персональной аутентификацией. Выбор T02 открыт; CLI не означает передачу issuer техническому оператору. Полноценный multi-organization UI не нужен. Код выдаётся один раз через no-store ответ, не попадает в аргументы shell, URL, logs или Git.

T01 описывает 128-bit CSPRNG секрет, keyed hash, срок/попытки/reissue, проверку получателя и канал компрометации. Все значения PROPOSED. Нельзя назначить пароль пользователю, узнать прежний пароль или автоматически восстановить отозванный membership. Общий Identity в A+B нельзя захватить через администратора только A: cross-organization linking/recovery требует отдельной проверки пользователя и account-security полномочия по плану L1; ordinary org issuer может инициировать, но не обходить эту границу.

Юридические основания/представительство/consent до реальных данных требуют PD review. Synthetic activation не создаёт фиктивного legal basis/consent. Production privacy workflow не входит в L1. Тесты: partial/concurrent provisioning, duplicate roster key, issuer self-escalation, cross-org recovery, code replay/expiry, session revoke, отсутствие implicit enrollment. Состояния и контракты — в плане L1, ничего здесь не реализовано.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.
