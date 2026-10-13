# G02/A06 — OWNER-DECISION

2026-10-10. G02/A05 B/C DIRECTION ACCEPTED; G02/A06 DOCUMENTATION AUTHORIZED — основание: явное задание владельца A06. Все SHALL/«должен» ниже — предложенные обязательства, NOT ACCEPTED / NOT IMPLEMENTED / NOT PROVEN. Новая норма, архитектурная квалификация и разрешение реализации требуют отдельных решений.

**A06 NORMATIVE PACKAGE READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**

## Что подготовлено

Принято владельцем только направление B/C и подготовка A06. Исторический A05 draft с фразой «решение ещё не принято» сохранён как evidence; этот документ фиксирует новое решение из задания, не переписывая историю.

- [Точный normative diff ND-01–ND-10](L1-G02-A06-NORMATIVE-DIFF.md): старый текст, replacement, причина, security impact, acceptance и proof gaps.
- [Failure model и семь гарантий](L1-G02-A06-FAILURE-MODEL.md): PostgreSQL, last-controlled-egress, storage isolation, recovery и E03 conflict.
- [P1–P9, blockers и R0–R4](L1-G02-A06-PROOF-OBLIGATIONS.md): все обязательства OPEN, implementation/proof не заявлены.
- [S4-A/S4-B и граница S7](L1-G02-A06-S4-SPLIT.md): отдельные scopes и точные acceptance criteria.

## A04 preservation и локальный commit

Canonical `/home/tea/github/lms`; обнаружена ветка `codex/l1-s4-jwt-api-authorization`, исходный HEAD `a60ee6c73c4400bd1bdf6c6df434358372745c41`. Ветка не переключалась: исключение для A04 дано владельцем. Отдельный commit **`d8b1e8278df3316aa8b72f76afa0143f3ff8c780`** содержит только четыре A04 deliverables, provenance/archive и A04 checkpoint append. Предсуществующий checkpoint diff/test/прочие untracked материалы в commit не включены. A06 artifacts оставлены для review без дополнительного commit.

Все 336 предсуществующих tracked/untracked файлов, working/index patches и SHA-256 baseline сохранены в `/tmp/lms-g02-a06-preservation`. Семь SHA-256 staged A04 совпали с `/tmp/lms-g02-a05-baseline.json`; checkpoint-before совпал с original preservation manifest, staged checkpoint = before + append. Четыре deliverables скопированы byte-for-byte; текущий checkpoint только дополнен, его поздний текст сохранён.

[Repository provenance](evidence/L1-G02-A04/PROVENANCE.md) и [SHA256SUMS](evidence/L1-G02-A04/SHA256SUMS) закрепляют исходные файлы. Хеши подтверждают локальную непрерывность, не являются подписью автора/поставщика. Отличия от исторического A04 manifest были в `.obsidian/workspace.json` и `codex/L1-G02-OWNER-DECISION.md`; они сохранены. Старые A02–A04 observations, V1 FAIL/NOT ESTABLISHED, V2/V3 NOT SATISFIED не изменены.

## Открытые blockers для решения

1. **B-PG:** нет проверяемого контракта против поздней видимости зависшего COMMIT. Precheck, второй publication COMMIT, post-hoc quarantine и ACK timeout не решают его. Блокирует direct domain publication и соответствующие runtime paths.
2. **B-EGRESS:** нет bounds независимого cutoff всех controlled buffers/kernel/NIC/retransmit и last-send oracle. Блокирует HTTP acceptance обоих scopes.
3. **B-ISOLATION/B-FENCE:** private namespace, immutable seal/exact version, retained handles, mutation-point fence/old primary/restart не доказаны.
4. **B-CLOCK/B-CAPACITY/B-RECOVERY:** numerical clocks/dispatch/quotas, no-replay authoritative resolution/replay horizon и safe purge остаются OPEN.
5. **B-E03:** требуется отдельное нормативное решение ниже. Новый смысл completion несовместим с действующим full-stop обещанием без такого решения.

## Отдельное решение E03-C — предложенный текст, НЕ ПРИНЯТО

> Для согласования E03 с предложенной редакцией T04 отдельно принимаю следующую замену только семантики completion: SecurityBlocked запрещает login/issue/refresh, атомарно отзывает все refresh sessions и фиксирует audit. Unblock — отдельное подтверждённое действие, old grants/sessions не восстанавливаются. Уже linearized access JWT допускает новые admission лишь до true exp≤15 минут. Каждый started request ограничен HTTP≤60s ordinary/≤480s designated upload/download, а новый domain effect/publication — исходным operation deadline и применимым authority cutoff. При доказанных time/publication contracts допустимые новые effects и controlled egress заканчиваются максимум через 23 минуты после block. Это не срок физического окончания доказанно изолированного private I/O и не срок выяснения unknown outcome: они могут оставаться pending, не меняя domain state, eligibility/claim, опубликованные bytes, доступ или egress. Recovery не продолжает expired command. Immediate revoke/mandatory denylist/Redis не вводятся. Current ProcessingRestrictions остаются отдельным требованием. Эта редакция не объявляет соответствующие contracts доказанными и не разрешает реализацию.

Проверен также [ADR-L17 §SecurityBlocked E03](../docs/adr/ADR-L17-account-security-recovery-challenges.md), действующий текст:

```text
JWT, выпущенный до block, остаётся до exp≤15m; операция перед exp может завершаться ещё ≤8m. При доказанных conservative clock/operation bounds ADR-L15 идеальный worst case до 23m после block, **не мгновенное прекращение и не двухчасовой ordinary revoke**. Без runtime evidence не заявлять безусловный wall-clock SLA. No mandatory denylist/Redis; block не отменяет committed learning history/скачанные bytes.
```

E03-C заменяет только completion-семантику этого абзаца и соответствующего фрагмента register. Все остальные запреты/транзакционная сериализация ADR-L17, включая Activation/Recovery consume и terminal challenges, сохраняются дословно.

Область будущей синхронизации E03-C: строка E03 реестра, ADR-L17 и E03 passages ADR-L15/ссылок. Пока ничего из этого не изменено. Владелец может отклонить E03-C: тогда full completion остаётся обязательным, B/C в текущем виде нельзя вводить. Принятие только направления A05 не означает E03-C acceptance.

## Решение N — нормативная редакция (предложение)

> После отдельного принятия E03-C принимаю предложенные ND-01–ND-09 вместе с нормативно включёнными A06 FAILURE-MODEL, P1–P9/R0–R4 и S4-SPLIT acceptance criteria как новую редакцию требований. Разрешаю отдельную документационную синхронизацию действующих документов с явной датой вступления в силу и сохранением A02–A04 evidence. 60/480/600/900/7200 и E01/E02/E06/O01 сохраняются. Это решение не квалифицирует deployment, не закрывает proof blockers и не разрешает реализацию S4-A/B.

Это только draft для будущего явного принятия. Не применять текст N условно или автоматически по факту чтения A06.

## Решение A — отдельное разрешение S4-A (рекомендация, НЕ ДАНО)

> Отдельно разрешаю только S4-A component/protocol implementation и synthetic verification по A06 S4-SPLIT A-AC1–5, с A-AC6 как integration stop. Shared PG/clock/egress obligations не отменены. File/storage/publication implementation S4-B, general ingress cutover и объявление S4 acceptance запрещены. До HTTP runtime acceptance представить доказательства B-PG/B-EGRESS/B-CLOCK для соответствующих путей. Scope не включает S5–S8, migration M2/M3, production migrations, реальные персональные данные или push/merge/deploy.

Рекомендация: рассмотреть такое ограниченное S4-A разрешение отдельно, чтобы проверить identity/JWT/O01/scopes, сохранив блокировку integration. Не рекомендовать public rollout либо S4 PASS до доказательства shared bounds. S4-B требует собственного architecture/schema/API review и нового разрешения, не следует автоматически из A или N.

## Предлагаемая приёмка и текущий статус

S4-A — A-AC1–6; S4-B — B-AC1–7; общий S4 — оба scopes с обязательными file use cases и R0–R4, E03-C и независимым security/DDD review. Сейчас R0 частично выполнен только архивированием A04; нормативное approval pending. R1 NOT ESTABLISHED, R2 NOT IMPLEMENTED, R3/R4 NOT RUN. Неопределённый measurement/trace — не PASS.

Документационная проверка описана ниже. Backend regression и fault experiments NOT RUN: runtime/schema/config/tests в A06 не изменялись. Новое vendor research не выполнялось; A04 внешние источники сохранены как исторические, их нынешние контракты не утверждаются. Production/реальные ПД, S4 implementation, S5–S8, M2/M3, gateway integration, push/merge/rebase/deploy не выполнялись.

## Документационная проверка A06

PASS: пять artifacts; P1–P9 дословно из A05; все действующие цитаты diff сверены с исходниками; локальные ссылки вне fenced historical quotations разрешаются; A04 SHA256SUMS совпадают; `git diff --check` без ошибок. Исходные normative/runtime/test файлы byte-for-byte сохранены; checkpoint ровно baseline + A04 append. HEAD/ветка после разрешённого A04 commit неизменны, index пуст.

Из 336 предсуществующих файлов обнаружено одно внешнее изменение `.obsidian/workspace.json` во время работы. A06 этот файл не редактировал и не восстанавливал; его исходная копия сохранена. Полная неизменность всего дерева поэтому не заявляется. Первая проверка ссылок ошибочно учитывала relative links внутри дословных code-fenced цитат исходников; исправлена область проверки, исходные цитаты не изменялись. Проверка сохранности сначала остановилась на внешнем Obsidian изменении; оно явно выделено, не скрыто. Локальный отчёт и hashes пяти artifacts: `/tmp/lms-g02-a06-preservation/validation.json`; validator: `/tmp/lms-g02-a06-validate.py`. Это documentation PASS, не runtime/security proof.

## Остановка

После рассмотрения владелец отдельно решает E03-C, нормативную редакцию N и scope A/B. До следующего решения работы остановлены.

**A06 NORMATIVE PACKAGE READY FOR OWNER DECISION · T04 UNCHANGED · S4 BLOCKED.**
