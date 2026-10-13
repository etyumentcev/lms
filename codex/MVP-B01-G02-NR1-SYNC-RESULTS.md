# MVP-B01/G02-NR1 — documentation synchronization results

2026-10-11. **DOCUMENTATION ONLY · SYNC OWNER ACCEPTANCE PENDING.** NR1 нормативно принято с **2026-10-11** явным owner decision текущего задания. Технически **NOT IMPLEMENTED / NOT ACCEPTED**. Это изменение temporal completion guarantees только controlled SyntheticOnly, не доказательство прежнего T04 и не принятие A06 E03-C/N.

## Нормативный diff и scope

Принятый источник — неизменённые [N1–N7 и точный D1–D8](MVP-B01-G02-NORMATIVE-REVIEW.md). Применена предусмотренная review append-only запись владельца и effective notices; исходные clauses не удалены. Notices имеют приоритет над историческими PROPOSED/NOT ACCEPTED/unchanged headlines только в явно указанной части. Route-by-route technical acceptance ещё отсутствует; неидентифицированный path blocked.

| Файл | Точное назначение изменения |
|---|---|
| [Реестр решений](L03-OWNER-DECISION-REGISTER.md) | Append-only owner acceptance, effective date, N1–N7 by reference, D1–D8 scope; оба точных replacement текста D8; неизменяемые требования и blockers |
| [Normative review](MVP-B01-G02-NORMATIVE-REVIEW.md) | Effective notice принятия; N1–N7, D1–D8, исходный проект §9 и историческая validation сохранены |
| [ADR-L15](../docs/adr/ADR-L15-jwt-delayed-revocation.md) | D1 response/full-operation→N1/N4; D2 transaction→N2/N3; D3 file/stream→N3/N5; D4 formal proof→N1/N2/N4/N6; D8 derivative 23m; старые тексты сохранены |
| [ADR-L16](../docs/adr/ADR-L16-organization-jwt-browser-session.md) | Уточнение связанной T04/E03 ссылки, без замены E01/E02/O01/credential contract |
| [ADR-L17](../docs/adr/ADR-L17-account-security-recovery-challenges.md) | D5 только completion E03→N4; первый абзац SecurityBlocked, E04/E05 сохранены |
| [ADR-L18](../docs/adr/ADR-L18-synthetic-processing-restrictions.md) | D7 temporal stream dependency→N5, current restrictions/write serialization/jobs сохранены |
| [G02 bounded execution](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md) | D6 I6→N1–N3/N6; производные completion выводы scoped; I1–I5/I7 safety сохранены |
| [G02 failure model](../docs/architecture/L1-G02-FAILURE-MODEL.md) | Successor notice, исторические A02 failure results не переписаны |
| [Guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md) | Notice только об изменении temporal status; прочие правила не пересмотрены |
| [Reconciliation](MVP-GUARDRAILS-RECONCILIATION.md) | Notice scoped supersession, без закрытия R01–R04 |

Дополнительно созданы этот отчёт и [шестишаговый roadmap](MVP-B01-G02-NR1-IMPLEMENTATION-ROADMAP.md). [Точный unified diff](evidence/MVP-B01-G02-NR1/documentation.diff) сравнивает с фактическим dirty baseline начала задания, включая untracked документы; это не diff против HEAD и не присваивание посторонних изменений. Diff включает десять синхронизированных документов и оба deliverables. [Preservation manifest](evidence/MVP-B01-G02-NR1/preservation.json) содержит исходные SHA-256, hashes неизменённого evidence и Git identity. Сам manifest и diff — дополнительные evidence artifacts; diff не включает себя.

## Consistency и неизменяемые границы

- True JWT lifetime≤900s; conservative expiry без positive leeway; exp≤AccessValidUntil−600s; delayed revocation и D≤request+7200s, E01/E02/O01/E04/E05 сохранены. CLOCK NOT PROVEN — обязательный самостоятельный blocker, не снятый NR1. Нет безусловного wall-clock SLA при unknown clock.
- S2/S3, org/course/group scope, current ProcessingRestrictions и сериализация всех relevant writers, atomic mutation/result/audit, immutable history, exact immutable file versions/parent authorization и новый admission для каждого запроса сохранены. Нет новых runtime assertions о выполнении этих обязательств.
- Late COMMIT допустим только для фиксированного ранее допустимого набора learning mutations по N2. Нет автоматического распространения на credential issuance, grants, B08 publication, general jobs или внешние irreversible effects. L15 jobs paragraph, L17 первый SecurityBlocked paragraph и полный L16 body сохранены.
- Unknown outcome≠rollback; automatic replay/resume запрещён. Late queued bytes и отсутствие универсального recovery RTO явно приняты, safe finite pending accounting обязателен. Quiescence нужна для seal/purge; timeout не освобождает ресурсы.
- A02–A06/T04/E03 historical failure evidence сохранены. A06 E03-C/N и ND-01–ND-10 не становятся новым контрактом. P1 NO-GO не закрыт и не квалифицирован повторно. Старые FAIL не исправлены и не стали PASS.

## Проверки и preservation evidence

Воспроизводимая проверка текущего задания: `python3 /tmp/nr1-sync/validate.py` из `/home/tea/github/lms`. Baseline и точные исходные тексты сохранены в `/tmp/nr1-sync`; постоянный итог — manifest и diff по ссылкам выше. Проверка выполняется после формирования отчёта; её фактические счётчики записаны в manifest.

Проверяются: byte-for-byte исходный prefix реестра; восстановление каждого исходного документа удалением единственного notice; неизменность N1–N7/D1–D8; семь дословных original-text blocks D1–D7 в исходных ADR/G02; наличие локальных Markdown link targets; сбалансированные code fences и отсутствие trailing whitespace; SHA-256 остальных baseline файлов в codex/docs/src/tests/tools и корневых файлов; branch/HEAD/index hash. Fragment anchors не добавлялись; web links не перепроверялись.

Branch сохранён: `codex/l1-s4-jwt-api-authorization`, HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. Это существующий dirty checkout, а не main; переключение не выполнялось. Посторонние runtime/schema/test/content edits не изменены. `.obsidian` и ignored build outputs не входят в baseline coverage; полная неизменность всего filesystem не заявляется. Commit/merge/rebase/push/deploy не выполнялись.

Фактический результат `python3 /tmp/nr1-sync/validate.py`: **PASS** — 112 local link targets, 7/7 historical blocks; 7 880 прочих baseline файлов без изменений, из них 152 evidence-файла в отдельном hash manifest; branch/HEAD/index unchanged. Все девять notices обратимы без потери исходного текста; prefix реестра сохранён побайтово.

Runtime/unit/integration/negative-access/security/fault/browser tests **NOT RUN**: scope документационный. Исторические PASS не являются новым прогоном. Проверки документов не дают G02/security acceptance.

## Guardrails §7

| Проверка | Результат / предел |
|---|---|
| Actor/use case/invariants/states | covered — owner→scoped normative sync; application/domain/physical states заданы N1–N7, roadmap задаёт будущие deliverables |
| S2/S3/PRS1 и gates | covered — нормы сохранены; exact OFF, cutover закрыт |
| Server org/course/group/ownership | covered нормативно; runtime проверка N/A — нет новых access paths |
| Subjects/sources | covered — current deny и processing evidence обязательны, narrowing не введён |
| Lists/counts/aggregates | covered нормативно; runtime N/A — queries не менялись |
| Attachments/history | covered — N3/N5, original docs preserved; новые file paths не реализованы |
| Transactions/locks/CAS/retry/unknown | covered в принятом контракте; полный writer lock graph и implementation evidence BLOCKED до соответствующего slice |
| Validation/limits/errors | covered — N1/N3/N4; profile caps/clock evidence NOT PROVEN |
| Unit/integration/negative/E2E | NOT RUN — documentation only; будущие AT01–AT12 не объявлены PASS |
| Неавторизованные изменения | covered — только перечисленные docs/evidence; runtime/schema/DI/HTTP/Docker/edge/processing guards не менялись |
| Real data/secrets/release claims | covered — SyntheticOnly; unsupported readiness не заявлена |
| Фактические проверки/remaining gaps | Documentation validation и preservation — см. manifest; clock, storage, recovery, route/security evidence открыты |

Специализированные model-domain/implement-use-case/implement-feature skills в текущем каталоге доступных skills отсутствуют; их выполнение не заявляется. Изменение кода здесь не требовалось.

Остановлено на owner acceptance документационной синхронизации. Следующий отдельно авторизованный инженерный slice — минимальный clock component с проверяемым кодом и evidence; бесконечный G02 qualification цикл не создаётся.

**NR1 NORMATIVELY ACCEPTED · G02 IMPLEMENTATION BLOCKED · CLOCK NOT PROVEN · H04/READY BLOCKED · B08 NOT AUTHORIZED · PRS1 EXACT OFF · S4 HTTP CUTOVER NOT AUTHORIZED.**
