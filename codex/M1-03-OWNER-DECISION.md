# M1-03 — запрос решения владельца

Дата: 12.10.2026  
Статус: **ACCEPTED 12.10.2026 — `ПРИНЯТЬ M1-03-BOOTSTRAP-01`**

## Обнаруженный блокирующий конфликт

Карточка M1-03 требует отдельного owner approval для platform bootstrap и org-admin capabilities. Принятые T02/T03 задают бизнес-инварианты и существующие `IdentityUser`, `ActorIdentity`, `Organization`, `OrganizationMembership`, но не задают исполнимый ingress:

- текущий access JWT допускает только `Student`/`Teacher`; membership с `EducationalRole = null` и `PermissionFlags > 0` не получает административного access token;
- значения четырёх `PermissionFlags` не определены как продуктовые capabilities;
- доверенная аутентификация первого platform operator отсутствует;
- T01/T02 требуют закрытое provisioning/activation, а M1-04 ещё не разрешён; самовольный initial password или новый activation flow изменил бы security contract;
- актуальный owner checkpoint оставляет S5–S8 запрещёнными, а указанного в задании `codex/SAPIENTUM-M1-PLAN.md` в checkout нет. Доступна только карточка в `codex/Sapientum_M1_Codex_Tasks_All.md`.

## Один конкретный вопрос владельцу

**Принять ли решение `M1-03-BOOTSTRAP-01` в следующей точной границе?**

1. Только `SyntheticOnly`. Platform operator аутентифицируется JWT существующего configured authority с отдельными audience/capability `sapientum-platform` / `organization:bootstrap`; этот JWT не содержит и не выдаёт образовательных scopes и не разрешает чтение курсов, работ или файлов.
2. Org-admin — существующие `IdentityUser` + `ActorIdentity` + `OrganizationMembership`: `EducationalRole = null`, новый именованный бит `PermissionFlags.ManageOrganization = 1`. Он не добавляется в `ScopeRole`/`AuthorRole`, не получает Student/Teacher grants и не читает учебные ресурсы.
3. Разрешить additive schema только для имени организации и идемпотентного bootstrap receipt/command id; неизменяемый `SecurityAudit` остаётся журналом. Никакой второй модели Organization, пользователя, membership или audit.
4. Разрешить защищённые HTTP-операции и React UI только для: create/list own bootstrap result, create first org-admin и однократной активации именно первого org-admin по параметрам T01 (16 Crockford Base32 / 80 бит, 24 часа, HMAC, секрет показывается один раз, `Cache-Control: no-store`). Roster/Student/Teacher/recovery/groups исключены и остаются M1-04+.
5. Одна serializable PostgreSQL transaction владеет create: lock bootstrap command key → duplicate/idempotency check → Organization → IdentityUser/Actor/ActorIdentity → OrganizationMembership → activation challenge → SecurityAudit → receipt → commit. Повтор того же key возвращает тот же публичный receipt без повторного секрета; другой payload — conflict. Потерянный commit ACK проверяется по receipt, мутация автоматически не повторяется.
6. Platform operator может создавать организации, но не выдавать себе membership; org-admin видит и администрирует только свою организацию и не может создавать вторую организацию. Все foreign-org selectors и forged capabilities отклоняются сервером до раскрытия данных/counts.

Варианты ответа: **ПРИНЯТЬ M1-03-BOOTSTRAP-01** либо **ОТКЛОНИТЬ** с указанием заменяющего platform ingress / org-admin capability / first-admin activation contract.

## Фиксация исполнения

Решение принято владельцем точной фразой `ПРИНЯТЬ M1-03-BOOTSTRAP-01`. Реализация и доказательства находятся в `codex/M1-03-RESULTS.md`; исходная формулировка выше сохранена как нормативная граница. M1-04 этим решением не разрешён.
