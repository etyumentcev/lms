# G02/A04 — Owner decision package

2026-10-09. **V1 CONTRACT EVIDENCE BLOCKED.**

A04 завершён как один конечный research-only этап. Рассмотрен один полный кандидат C04-1: AWS S3 Object Lock, RDS PostgreSQL17.6 Multi-AZ single standby, EC2/Envoy1.35.0, внешний Step Functions supervisor и Amazon Time Sync. Проверены первичные технические документы и version-tagged Envoy/Linux sources. Конфигурация не создана, gateway не реализован, эксперименты и cloud API/provisioning не выполнялись.

## Решение по доказательству

Практически приобретаемые компоненты найдены; **конфигурация с достаточными проверяемыми контрактами V1 не установлена**. Это вывод о C04-1 и рассмотренных источниках, не доказательство невозможности T04 на любой инфраструктуре. Неизвестные managed backend versions, AMI/build/capacity и region availability дополнительно исключают квалификацию deployment.

Блокирующие причины, источники и ограничения находятся в [матрице](L1-G02-A04-CONTRACT-MATRIX.md). Ключевые: Object Lock не проверяет LMS epoch при mutation; outstanding multipart writes не завершаются гарантированно от abort; нет finite PG WAL/rollback/termination bound; Envoy stream reset не закрывает end-to-end egress proof; внешний supervisor и clock source не дают численных dispatch/cutoff/uncertainty контрактов. Поэтому [worst-case ledger](L1-G02-A04-TIMING-LEDGER.md) не доказывает ни 60s, ни 480s. Полный [профиль и реестр источников](L1-G02-A04-CANDIDATES.md) фиксируют версии и пределы каждого утверждения.

## Варианты отдельного решения владельца

| Вариант | Конкретное решение | Допустимость после A04 |
|---|---|---|
| A | Продолжить с инфраструктурой, для которой V1 доказан | **Недоступен для C04-1:** V1 не доказан. Нельзя разрешить реализацию на основании одних WORM/HA/timer свойств |
| B | Отдельно изменить поддерживаемую модель отказов, точно перечислив исключаемые hangs/partitions/control-plane/queue failures и оставшиеся stop guarantees | Только отдельным решением владельца; A04 ничего не исключил. Простое исключение глобальной катастрофы недостаточно, оно уже есть в baseline |
| C | Отдельно пересмотреть формализацию T04, явно задав, что считается completion и last controlled send, и последствия для security/revocation | Только отдельным решением владельца; 60/480/600/900/7200 и текущий physical completion oracle здесь сохранены |
| D | Признать выбранный deployment непригодным под неизменные требования и прекратить G02 | Обоснованный вариант при сохранении исходных требований и отсутствии дальнейшего исследования. Владелец ещё не выбрал D; агент не закрывает G02 сам |

Рекомендация при неизменной модели/T04: не принимать C04-1; рассмотреть D. B/C — самостоятельные изменения требований с отдельной оценкой последствий, а не уже выданное разрешение. Новый цикл поиска и реализация не начинаются автоматически.

## Gate и границы результата

| Gate / scope | Состояние |
|---|---|
| A02 option C | DESIGN BASELINE ACCEPTED |
| V0 | Owner разрешил A04 research only; не runtime/prototype cutover |
| V1 | FAIL / NOT ESTABLISHED — V1 CONTRACT EVIDENCE BLOCKED |
| V2 / V3 | NOT SATISFIED; новая реализация, adversarial integration и security review не выполнялись |
| S4 | BLOCKED |
| S5–S8, full S7 lifecycle, M2/M3 | NOT AUTHORIZED |
| Runtime / migrations / реальные ПД / push / merge / deploy | Не выполнялись |

Проверки A04 ограничены документами: первичные источники, внутренняя согласованность статусов и ledger, локальные ссылки, сохранность прежних файлов и append-only S4 checkpoint. Backend regression не запускался: runtime/test code не менялся; прежние passing tests не сертифицируют C04-1. Независимый review не проводился.

**Стоп после отчёта. V1 CONTRACT EVIDENCE BLOCKED.**
