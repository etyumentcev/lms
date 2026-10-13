# L1/S4-A4 — Transaction and concurrency protocol

2026-10-10. Documentation only; synthetic fixtures only. **EXISTING** — подтверждено исходным кодом; **ACCEPTED** — принятая норма PRS1; **PROPOSED** — решение A4, ещё не принято; **NOT PROVEN** — обязательство будущей проверки, не результат теста.

## Исходное состояние и выбранная граница

**EXISTING:** [E3/E7/E8/E9](L1-S4-A4-IMPLEMENTATION-DESIGN.md) ссылаются на точные методы. Current gate создаёт RepeatableRead snapshot, Save начинает отдельную transaction; обычный повторный SELECT в таком snapshot не устраняет stale allow. S3 уже использует organization `FOR UPDATE`; issuer берёт его после S2 locks.

**PROPOSED:** все PRS1 операции одного org используют exclusive organization row lock как общий mutex. Это заведомо грубый, но минимальный проверяемый протокол; никаких per-resource/advisory locks вместо него в первом варианте. Он защищает также отсутствие restriction row и будущие insert phantoms. Throughput и длительность ожидания **NOT PROVEN**, оптимизация только отдельным design.

Fresh dedicated DbContext, явный `READ COMMITTED`. Discovery до lock допустим только для поиска org/IDs, не для admission. После lock — новые запросы AsNoTracking/очищенный tracker и повторная проверка org route. RepeatableRead, начатый до ожидания org lock, запрещён: он может сохранить старый snapshot. Каждое protected состояние читается после lock, а все его writers обязаны взять тот же lock **до любого DML**. Это и есть доказательство актуальности, а не повторный SELECT сам по себе.

## Полный порядок locks

Если operation действительно использует S2 account/session capability: `AccountSecurityState(UserId)` → `AspNetUsers(Id)` → `ActorIdentities(UserId)` → `IdentitySessions(SessionId)` → `Organizations(OrganizationId)` → membership (ActorId sorted) → enrollment (CourseId ordinal, StudentId) / teacher grant (GrantId) → bindings (CourseId ordinal, EffectiveFrom) → restrictions (RestrictionId sorted) → assignment (Id) → work (Id) → child metadata (kind, Id) → command receipt/audit inserts. Минимальная educational mutation начинается с org, не читает security/session заново и не вводит immediate E03/T04 revocation. S3 frozen ownership и token `ScopePolicy` сохраняются.

После org запрещено вызывать `PostgresIdentitySessions`/issuer или брать account/session locks. Trusted admin capability проверяется до org либо под S2-prefix locks в том же порядке; educational JWT не административная capability. Новые PRS1 operations single-org; cross-org command reject. Все множества ключей известны до дополнительных locks, расширение требует abort/replan, не захват в обратном порядке. Educational receipt `Prs1MutationReceipts` уникален по org+command ID; administrative receipt — `Prs1Commands` с глобальным CommandId и проверкой org; discovery receipt не permission.

S3 `s3_change_authority` берёт org до membership/scope, совместим с этим порядком. `s3_capture_assignment_scope` берёт org в AFTER INSERT trigger: будущие callers обязаны брать org **до INSERT**, иначе implicit FK/row locks могут инвертировать порядок. Наличие trigger само по себе не доказательство полного lock order. Аналогично все writers Works/history/Message/binding/context/attachment scope/course mapping обязаны быть инвентаризированы. Прямой DML у serving roles должен быть закрыт или writers отключены. S2 prefix и S3 протоколы не переписываются. Lock graph + restricted-role tests являются gates, не заявлены доказанными.

## Основной алгоритм mutation

1. Проверить command shape/idempotency ID, supported complete plan, локальную token trust/lifetime; открыть fresh ReadCommitted transaction. Не принимать сохранённый `TokenAuthority` после distrust без повторной validation по SR-02.
2. Взять нужный prefix и org `FOR UPDATE`; убедиться Enabled/SyntheticOnly. Проверить receipt с тем же command ID и digest: конфликт payload → reject; committed receipt не является свежим разрешением выдавать content.
3. Перечитать S3 chain/frozen generations; пересобрать plan, source graph и subjects. Взять assignment/work lock, проверить expected work version; при new work assignment lock защищает отсутствие work вместе с existing unique constraints. Recheck domain latest/unreviewed, assignment/lesson availability.
4. Прочитать qualified clock interval после ожиданий; одно binding должно покрывать весь interval, same org, context SyntheticOnly/effective. Прочитать все active restrictions текущего ContextId с provenance/decisions/receipts; не фильтровать по ResourceRef/Operations до classification. Допустим subject prefilter только после доказанного полного set; минимальный adapter читает весь context.
5. Pure evaluator: unknown plan/subjects/binding → deny; legacy OR verified exact. ScopePolicy и domain validation — независимые необходимые условия. Invalid provenance → conservative, DB errors → deny/exception.
6. Непосредственно перед первым data DML перечитать current binding/restrictions/provenance и clock, revalidate plan/subjects/versions. Locks уже не допускают intervening writers; второй read служит проверкой реализации/времени, не заменяет mutex. Не использовать aggregate, загруженный до locks.
7. Выполнить только локальные writes, domain journal, immutable `Prs1MutationReceipts` (Submit/Review) либо `Prs1Commands` (administration) и audit в той же transaction. Проверить clock/expiry/binding interval снова до commit; wait после этого не считается bounded execution proof. Никаких публикаций, storage, callbacks с внешними effects.
8. Commit. Только acknowledged commit + завершённый disposal + final token/clock validation допускают выдачу component result. Postcommit validation failure может означать committed mutation без возвращённого результата; не объявлять rollback. Не выполнять повторный effect.

**Linearization point:** успешный PostgreSQL COMMIT при удержании org lock; final locked evaluation — admission point. Mutex задаёт порядок конфликтующих transactions вплоть до commit/abort. Начало HTTP/request не задаёт приоритет. Restriction после commit mutation не отменяет историю; restriction до получения mutation lock обязана участвовать в её evaluation. Время/clock может измениться без DB writer: lock не доказывает срок внешнего завершения.

## Протоколы по сценариям

Во всех строках boundary = одна fresh ReadCommitted transaction; org lock удерживается до commit/rollback; общий failure protocol ниже обязателен.

| Сценарий | Lock suffix, повторные данные | Linearization и TOCTOU |
|---|---|---|
| Issue legacy | org→command; проверить admin/org/context/subject, новый ID; вставить active row + birth + SecurityAudit с уникальным AuditId=command ID (legacy path не создаёт Prs1Commands) | Commit делает запрет видимым; reader ждёт тот же org. Нет provenance → всегда conservative |
| Issue PRS1 | org→new restriction/command; canonical/parser/root/subject membership/evidence, snapshot equality | Restriction+birth+sidecar+receipt/audit один commit. Deferred pair check; исключение между insert откатывает всё |
| Withdraw restriction | org→restriction ID; перечитать State/Version; expectedVersion CAS, Active→Withdrawn, Version+1, audit/receipt | Только commit снимает этот запрет; чужие active rows остаются OR. Не delayed ordinary authority revoke |
| Reissue/narrowing | org→old restriction; перечитать decision authorization, old version, current binding/root/subjects; создать новый ID+provenance, withdraw old CAS | Один commit, никакого промежуточного состояния для readers. Stale old version откатывает new тоже; legacy semantic fields не меняются |
| Current read | org→projection metadata; повторить full closure/subjects/binding/restrictions, clocks, материализовать только разрешённое | Read transaction commit задаёт локальный порядок чтения, но не сетевой egress. Нет streaming или positive cache; release не является будущим permission |
| Submit | org→assignment→work; expectedVersion, all attempts, current affiliation/context/restrictions и plan 3 | Commit effects; restriction winner first → deny; Submit winner first → commit перед restriction. Не использовать current gate bool из другой transaction |
| Review | org→assignment→work→target submission; exact SubmissionId, latest/unreviewed, teacher/student/sources, plan 5 | Withdraw winner first может снять конкретный deny; Review first с активным запретом deny без effect, затем withdraw. Domain deny не снимается |
| Same ContextId/new version | org→binding; перечитать open interval/context effective и все old-context restrictions | Close+insert atomic. OriginVersion immutable и не сравнивается с новой current version для запрета |
| Different ContextId | org→bindings→old/new restrictions sorted; explicit transfer decision, полный inventory, coverage и old versions | Минимальный slice REJECT transition. Будущий adapter только atomic transfer+binding; отсутствие решения/полноты deny. Нельзя перевести course в пустой context |
| Affiliation/group move | существующий S3 org→membership→enrollment protocol; mutation после lock заново S3-resolves assignment | Commit S3 move упорядочен с PRS1; frozen group старых assignments не меняется. Не заменить token authority на mandatory fresh grant revocation |
| Course/scope/subject dependency change | org→соответствующие rows; reject unsupported reparent; metadata writers участвуют в том же протоколе | Пока writer не зарегистрирован/не fenced, affected operation deny. SQL FK/immutable checks остаются |

Scheduled binding: interval, пересекающий cutover, deny; после перехода новый binding/restrictions должны быть атомарно доступны заранее. Different ContextId scheduled transition также заблокирован в минимальном slice. Restriction origin effective version — provenance, не lifetime current restriction.

## Ошибки и восстановление

Cancellation до commit: abort/dispose, no result; если rollback подтверждён — no effects. Deadlock `40P01`, serialization `40001`, CAS conflict: transaction abort; повтор только всего протокола на новом context с тем же command ID после установленного abort. Lock/statement timeout: abort всей transaction, не продолжать с частично прочитанным plan. Числа timeout должны быть выведены из отдельно квалифицированного execution budget, не придуманы как гарантия G02.

Любая ошибка во время/после CommitAsync, connection loss или cancellation в неопределённой фазе → **Indeterminate**, а не success и не доказанный rollback. Новый connection на authoritative primary, тот же org lock: дождаться завершения прежней transaction, найти receipt по immutable command ID, сравнить exact payload digest/IDs. Для legacy issue сверять immutable SecurityAudit AuditId и root; для остальных — соответствующую таблицу receipt. Receipt есть → mutation committed, не повторять; content result требует нового read authorization. Receipt нет после полученного mutex и подтверждённого primary/restore epoch → прежняя transaction не оставила effect, полный retry разрешён только в пределах исходной admin decision. Если primary continuity/restore consistency неизвестна → остаётся blocked, administrative reconciliation. Не query lagging replica, не создавать новый command ID автоматически.

У receipt и sidecar atomic durability одной DB transaction; потеря локального client state не переводит legacy в PRS1. Crash до commit оставляет old restriction, crash после commit — replacement; recovery читает обе tables и ledger, не запускает compensating withdrawal. Rollback feature не rollback data.

## Граница доказательства

**NOT PROVEN:** DDL, permissions, полный writer inventory, deadlock stress, clocks, network failover/restore и fault harness ещё не проверены. Локальный протокол предотвращает stale DB admission только при полном участии writers и чтении authoritative PostgreSQL. Он не останавливает stream, не отменяет OS I/O и не доказывает ≤8 минут или 15-minute expiry при stalled commit/egress. T04/E03 прежние; G02 BLOCKED; SR-01–SR-04 остаются обязательными. S4 HTTP/S4-B/S5–S8 не начинаются.


**S4-A4 READY FOR OWNER DECISION · PRS1 ACCEPTED · IMPLEMENTATION NOT AUTHORIZED · T04/E03 UNCHANGED · G02 BLOCKED · S4 HTTP NOT AUTHORIZED.**
