# L1/S4-A3 — Independent Security Review

2026-10-10. **INDEPENDENT AGENT REVIEW PERFORMED — LIMITED ASSURANCE.**

Проверку выполнил отдельный AI reviewer `/root/independent_security_review`, которому не поручалась реализация A1/A2. Reviewer читал исходный код и тесты самостоятельно; отчёты автора использованы как описание scope, а не как замена проверки. Это разделение ролей между AI agents, а не независимый внешний человеческий аудит, организационная независимость или сертификация. Проверяющий не менял runtime, тесты, schema, Git или нормативные документы. Если владельцу требуется внешний reviewer, эта обязанность остаётся OPEN.

## Baseline и метод

Канонический каталог `/home/tea/github/lms`; HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. Проверены A1/A2 компоненты в этом HEAD и связанные S2/S3 authority/affiliation/SQL contracts. `git diff` относительно HEAD не показал изменений `src` или Component* tests. Посторонние изменения отчётов, `AttachmentDeadlineBoundaryTests.cs`, G02 документов и `.obsidian/` исключены из scope; reviewer их не изменял.

Метод: статическая трассировка parsing → verification → authority → resource → current processing; трассировка issuance locks → sign → commit → disposal → post-commit validation; чтение отрицательных и fault-injection тестов. Тесты reviewer повторно **не запускал**. 429/0/0 — принятый владельцем результат A2, не новый независимый прогон. Fuzzing, penetration testing, production infrastructure и реальные персональные данные не использовались.

## Вывод

Подтверждённых Critical/High/Medium/Low дефектов **в принятом ограниченном component scope** по этой проверке не установлено. Это не доказательство отсутствия дефектов. Ниже фиксируются три существенных ограничения интеграции и один нормативный пробел; severity `Informational / integration gate` не означает разрешение production/HTTP. Они не отменяют уже принятую компонентную приёмку и не требуют изменения кода в A3.

## Покрытие

| Область | Проверенное доказательство | Вывод и граница |
|---|---|---|
| JWT parsing/signing | `src/Lms.Infrastructure/ComponentJwt.cs:44–76,78–116`; `ComponentSigningKeys.cs:15–65`; `tests/Lms.Infrastructure.Tests/ComponentJwtTests.cs:77–115` | Ограничение 65536 chars, 3 сегмента, canonical base64url, duplicate/unknown field rejection, точные header/claims, fixed RS256, SPKI kid, issuer/audience, положительные поколения и согласованные scopes. Wrong-signature test использует доверенный kid, поэтому проверяет именно подпись. Нет remote key lookup/alg negotiation. Доверенные DTO не являются публичной границей ввода. |
| Lock order | `src/Lms.Infrastructure/PostgresIdentitySessions.cs:137–176`; `PostgresComponentTokenIssuer.cs:14–23`; `Migrations/20261009153218_ScopedAuthorityS3.cs:192–197,233,265` | AccountSecurityState → Identity user → ActorIdentity → session → organization; S3 mutations берут organization перед scope rows. Обратного ребра к S2 locks в просмотренном S3 пути не обнаружено. Это не глобальное доказательство deadlock freedom для будущих writers. |
| Transaction outcome | `PostgresIdentitySessions.cs:149–154`; `PostgresComponentTokenIssuer.cs:27–29`; `tests/Lms.Infrastructure.Tests/ComponentHardeningTests.cs:76–122`; `ComponentCommitAcknowledgementTests.cs:15–32,79–88` | Pending JWT не возвращается до commit и await-using disposal. Исключение/cancellation/unknown acknowledgement не преобразуется в успех. Wire test подавляет реальный COMMIT acknowledgement, а отдельное соединение проверяет persisted marker. Unknown outcome не объявляется rollback; автоматического replay нет. |
| Revocation races | `PostgresComponentTokenIssuer.cs:16–23`; `PostgresScopedAuthorityReader.cs:23–49`; `tests/Lms.Infrastructure.Tests/ComponentIssuanceTests.cs:24–108` | Fresh authority читается под organization lock; revoke/block, завершившиеся перед получением соответствующего lock, видны issuance. Выданный ранее JWT сохраняет E03 semantics до expiry. Per-request current-grant lookup не добавлен и не требуется этой приёмкой. |
| Clock uncertainty | `src/Lms.Application/ComponentAuthorization.cs:19–35,50–58`; `QualifiedAuthorityClock.cs:17–44,54–60`; `ComponentJwt.cs:70–72` | Earliest+900s/session/deadline−600s, округление вниз; whole interval nbf/exp, exact expiry deny. Deadline использует monotonic latest против initial earliest+budget; квалификация latch closed. Истинное включение времени в интервалы остаётся trusted source premise, deadline не останавливает worker/egress. |
| Signing-key trust | `ComponentSigningKeys.cs:16–65`; `PostgresComponentTokenIssuer.cs:28–29`; `tests/Lms.Infrastructure.Tests/ComponentKeyContractTests.cs:9–19` | RSA≥2048, proof of private signing capability, explicit verification-only registration, rotation overlap, StopSigning, terminal removal в instance. Sign/Verify проверяют fingerprint под ring lock. Ключи и clock — trusted host inputs; ring lock не защищает внешнюю мутацию RSA handle самим host. |
| Organization isolation | `src/Lms.Application/ScopedAuthority.cs:28–44`; `PostgresScopedAuthorityReader.cs:58–113`; `PostgresCurrentProcessingGate.cs:15–23`; `Migrations/20261009094756_SchemaFoundationsM1.cs:970–979` | Organization сравнивается в policy; affiliations выводятся с сервера через родителей, contradictory routing hints отвергаются; course/context org проверяются дополнительно. SQL FK/exclusion закрепляют org bindings и отсутствие перекрытия interval. |
| ProcessingRestrictions | `PostgresCurrentProcessingGate.cs:12–37`; `tests/Lms.Infrastructure.Tests/ComponentRestrictionHardeningTests.cs:13–75`; `ComponentAuthorization.cs:50` | Dedicated fresh RepeatableRead snapshot, no-tracking, SyntheticOnly, ровно один binding, caller/student/context restriction deny независимо от opaque selectors. После DB awaits повторно проверяется время binding. Attachment component access явно запрещён. Точные selectors/list/count/mutation semantics не реализованы. |

## Замечания и рекомендуемые действия

### SR-01 — Snapshot gate не гарантирует актуальность к моменту публикации

- **Severity:** Informational / integration gate; потенциально High при использовании результата как разрешения на публикацию реальных данных после изменения restriction.
- **Компонент:** `PostgresCurrentProcessingGate` и будущий read/mutation/stream adapter.
- **Доказательство:** `PostgresCurrentProcessingGate.cs:14` открывает RepeatableRead; строки 31–33 читают ограничения в snapshot; строки 34–37 повторно проверяют clock/binding values, но не более новый DB snapshot. Restriction, committed после создания snapshot, может не попасть в этот вызов. Это согласуется с комментариями строк 6–7 и заявленным A1/A2 component scope, а не доказывает нарушение принятого контракта.
- **Рекомендация:** до HTTP/mutation/stream integration нормативно определить момент принятия решения, freshness и in-flight semantics, затем отдельно авторизовать сериализацию/recheck или иной доказанный протокол; проверить races для введения restriction и смены binding во время чтения/публикации. Не утверждать, что повторный SELECT сам по себе решает TOCTOU.

### SR-02 — Key distrust применяется при Validate, а не к ранее созданному TokenAuthority

- **Severity:** Informational / integration gate; потенциально High при длительном кэшировании authority после компрометации signing key.
- **Компонент:** `ComponentJwt`, `TokenAuthority`, `ComponentAuthorization`, будущий adapter.
- **Доказательство:** `ComponentJwt.cs:57–58,73` проверяет trust и возвращает DTO; `ComponentAuthorization.cs:16–17,47–58` не хранит kid/trust epoch и проверяет scope/clock/processing. После `RemoveTrust` новый Validate отклонит токен, но уже полученный TokenAuthority автоматически не инвалидируется. В A1/A2 нет публичного adapter или positive authorization cache; воспроизведение угрозы в deployed system не заявляется.
- **Рекомендация:** определить время жизни validated evidence и поведение in-flight checks при trust removal. Для будущего adapter запретить перенос DTO между запросами, validating JWT заново в каждом admission; если требуется остановка уже выполняющихся операций, отдельно разработать trust-generation/fencing contract и tests. Не обещать мгновенную отмену всех in-flight действий текущим ring.

### SR-03 — Квалификация clock и lifecycle trust зависят от host

- **Severity:** Informational / integration gate; потенциально High при подаче недостоверных clock intervals или повторном доверии скомпрометированному ключу после restart.
- **Компонент:** `IAuthorityClock`, `QualifiedAuthorityClock`, `ComponentSigningKeys`.
- **Доказательство:** `ComponentAuthorization.cs:3–8` задаёт trusted interval interface; `QualifiedAuthorityClock.cs:3–12,31–44` проверяет внутреннюю согласованность, но не истинность UTC. `ComponentSigningKeys.cs:5–14,45–47` хранит distrust только в памяти; host владеет RSA handles. Последовательная mutation проверяется fingerprint, но concurrent external handle mutation не синхронизирована ring lock.
- **Рекомендация:** сохранить запрет production integration до независимых clock/source/uncertainty доказательств, политики владения неизменяемыми RSA handles, durable/distributed trust revocation и restart/rotation qualification. Эти работы не выполнять в A3.

### SR-04 — Subject closure и exact selectors не имеют принятого контракта

- **Severity:** Informational / normative decision required; потенциальная confidentiality/integrity значимость зависит от будущего принятого subject/list/operation contract.
- **Компонент:** `PostgresCurrentProcessingGate`, будущие selectors/list/count adapters.
- **Доказательство:** `PostgresCurrentProcessingGate.cs:28–33` намеренно игнорирует ResourceRef/Operations и включает context-wide/caller/StudentId ограничения. `ResourceOwnership` в `ScopedAuthority.cs:20–23` не перечисляет всех авторов review/message/других затронутых субъектов. Attachments запрещены A1/A2. Поэтому нельзя расширять утверждение «conservative» до всех будущих представлений, субъектов или агрегатов.
- **Рекомендация:** владельцу выбрать субъектный состав, inheritance, операции, list/count materialization и opaque compatibility в selector proposal; до отдельной приёмки сохранить существующую консервативную политику. Не реализовывать proposed exact interpretation и не ретроспективно объявлять его действующим требованием.

## Дальнейшее решение владельца

Зафиксировать факт отдельной agent review с указанным уровнем независимости; решить, требуется ли дополнительно внешний human review. Отдельно рассмотреть selector contract и integration gates SR-01–SR-04. Отсутствие подтверждённых дефектов не снимает G02 и не разрешает HTTP, реальные данные, production clock/KMS, schema или дальнейшие стадии. В A3 исправления кода не выполнялись.

**T04 UNCHANGED · E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
