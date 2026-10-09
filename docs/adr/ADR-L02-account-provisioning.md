# ADR-L02 — Closed account provisioning and recovery

2026-10-09. **ACCEPTED C01/C02 — бизнес-процесс**, owner: владелец, evidence: L0.3 §3. Параметры/минимальный org-admin UI **ACCEPTED T01/T02**. [Реестр](../../codex/L-DECISION-REGISTER.md), [план](../../codex/L03-L1-IMPLEMENTATION-PLAN.md).

Учётные записи по списку учеников/преподавателей создаёт уполномоченный администратор образовательной организации. Обязательных email/телефона и публичной регистрации нет. Активация — одноразовый код, переданный лично или по предварительно проверенному индивидуальному каналу; выдача не раскрывает других пользователей. Recovery — новый одноразовый код после проверки личности/полномочий, прекращение дальнейшего выпуска/refresh по согласованной security policy; уже выданные JWT действуют до exp по E02/E03, без новых учебных прав.

Это supersedes L0 operator-only issuer и объединение provision account с enrollment. Platform Management не даёт ни выдачи accounts от имени любой организации, ни чтения работ. Организационный администратор имеет отдельные scoped administrative permissions; Admin не добавляется в Domain.AuthorRole. Первого администратора организации создаёт уполномоченный оператор платформы через контролируемую процедуру и отдельный approved bootstrap manifest, а не через self-grant.

Фактическая база: [LocalDevelopment](../../src/Lms.Api/LocalDevelopment.cs) — demo seed, [AccessFacts](../../src/Lms.Infrastructure/AccessFacts.cs) — enrollment и teacher grants, account persistence отсутствует. Учебный grant и Identity credential — разные изменения. Новый account не создаёт membership/enrollment/assignment автоматически: каждый отдельный use case авторизуется и аудируется.

Принят минимальный org-admin UI без SSH/DB доступа: создание account, основная Student/Teacher role, activation/recovery, отдельные membership/enrollment/teacher assignments и блокировка/отзыв. Полноценная multi-org панель вне L1; CLI не альтернатива принятому UI. Код выдаётся один раз через no-store ответ, не попадает в URL/logs/Git.

T01 ACCEPTED: 16 символов Crockford Base32 / 80 бит CSPRNG, группы по четыре, HMAC-SHA256 с отдельным ключом и ротацией; activation 24 часа, recovery 15 минут, 5 неудач/код, проверки 10/мин/IP и 5/15 мин/account, выдача 3/час/получатель и 20/час/инициатор, cooldown 60 секунд; пароли 12–128. Полный [реестр](../../codex/L03-OWNER-DECISION-REGISTER.md). Общий Identity A+B нельзя сбросить единолично администратору A: обязательное независимое account-security подтверждение, flow E04 ACCEPTED по ADR-L17. Recovery не оживляет revoked grants; JWT lifecycle по T04/ADR-L15, emergency/reset semantics E02/E03 ACCEPTED по ADR-L16/L17.

Юридические основания/представительство/consent до реальных данных требуют PD review. Synthetic activation не создаёт фиктивного legal basis/consent. Production privacy workflow не входит в L1. Тесты: partial/concurrent provisioning, duplicate roster key, issuer self-escalation, cross-org recovery, code replay/expiry, session revoke, отсутствие implicit enrollment. Состояния и контракты — в плане L1, ничего здесь не реализовано.

Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.

L0.4 E04/E05: independent platform security operator (initiator≠approver), minimal operator UI, roster не proof, duplicate provisioning не bypass, code state/idempotency по [ADR-L17](ADR-L17-account-security-recovery-challenges.md). Реальные представители PD-G03 OPEN; synthetic fixtures не legal approval.
