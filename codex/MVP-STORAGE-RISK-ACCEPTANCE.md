# MVP storage — owner-authorized risk acceptance

2026-10-12. **PHYSICAL TAMPERING RISK ACCEPTED · FS-VERITY NOT REQUIRED FOR MVP · STORAGE-PROFILE-02 CANCELLED.** Источник полномочий — явное решение владельца в текущем задании. Только фотографии учебных решений Sapientum; разработка/проверка только в изолированном SyntheticOnly. Production инфраструктура не выбрана; G02 production BLOCKED.

## Точный нормативный override

Эта поправка имеет приоритет только над перечисленными storage clauses; исходные тексты и evidence сохраняются как история.

| Источник / прежняя норма | Действующая редакция только для фотографий решений |
|---|---|
| NR1 N3, первый абзац: «Только доказанно неизменяемая exact version с подтверждённой durability/hash/length может стать eligible. Retained writer handles и issued writes должны быть исключены до seal/claim; rename/chmod/hash сами по себе этого не доказывают. … Если immutability не доказана — quarantine, путь blocked.» | Eligible только после успешного завершения private upload: ограниченный и проверенный файл полностью записан, writer приложения закрыт, уникальный server-controlled key опубликован без overwrite, metadata подтверждены. Pending/failed/unknown uploads не выдаются. Приложение не перезаписывает и не переиспользует ключи оригиналов; попытки сохраняют собственные ссылки. Физическая неизменяемость bytes, fs-verity/kernel seal, retained FD/mmap exclusion proofs и квалификация filesystem машины разработчика не являются MVP prerequisite. Риск физической перезаписи после подтверждения принимается; length/format checks выявляют часть повреждений, но не доказывают отсутствие tampering. |
| NR1 N5: «pending/unverified файлы не выдаются» | Сохраняется; verified означает подтверждённый завершённый upload по редакции выше, а не kernel seal. Parent/scope/current processing/JWT/deadline проверяются на каждом запросе. |
| NR1 N6: «immutable storage isolation» | Для этих фотографий — private application-level isolation, уникальные ключи без overwrite, подтверждение upload, независимая авторизация родителей. Physical tampering исключён из требуемой модели защиты по принятому риску. Остальная acceptance-модель N6 неизменна. |
| ADR-L15 D3 / NR1 N3,N5; ADR-L18 file dependency; G02 bounded execution I3, seal receipt и overwrite/FD proof clauses | В этой узкой части ссылки на seal/exact immutable version читаются как подтверждение завершённой записи выше. Это не ослабляет требования domain commit, immutable history, обработки unknown, current restrictions или temporal gates. |
| STORAGE-SEAL-01 / STORAGE-PROFILE-01 вывод о необходимости fs-verity и STORAGE-PROFILE-02 | Исторические NO-GO/FAIL остаются достоверными результатами старого контракта. STORAGE-PROFILE-02 отменён, дальнейшая квалификация filesystem разработчика не требуется для MVP photo development. |

Не изменены N1/N2/N4/N7 и остальные положения N3/N5/N6: запрет automatic replay/resume неизвестного результата; finite capacity и учёт pending; неизвестный исход не rollback; сохранение unknown originals; отдельная безопасная cleanup; deadline/clock; S2/S3, Organization/Course/Group/Student/Teacher, current ProcessingRestrictions и родительская цепочка; численные ограничения файлов/ресурсов; atomic claim/domain/history/audit. Не вводятся права из одного course grant. Никаких public/static/presigned URL. Backup/recovery остаются отдельными эксплуатационными требованиями ADR-L05: согласованная копия DB+originals, проверка восстановления и явно принятые RPO/RTO; данное решение не объявляет их выполненными и не требует выбора production FS сейчас.

## Дословная замена первого абзаца N3 в указанном scope

Прежний текст (остается в исходном документе как история):

```text
Storage/worker без domain credentials пишет только в уникальный private namespace старого operation/generation; namespace не переиспользуется. Только доказанно неизменяемая exact version с подтверждённой durability/hash/length может стать eligible. Retained writer handles и issued writes должны быть исключены до seal/claim; rename/chmod/hash сами по себе этого не доказывают. Late worker output не создаёт Ready/parent/reference/permission. Если immutability не доказана — quarantine, путь blocked.
```

Действующий текст только для фотографий решений Sapientum:

```text
Storage/worker без domain credentials пишет только в уникальный private namespace старого operation/generation; namespace не переиспользуется. Для фотографий учебных решений Sapientum eligible становится только полностью завершённая private запись: формат и размер проверены, запись/flush/закрытие writer приложения успешно завершены, уникальный server-controlled key опубликован без overwrite, metadata подтверждены. Физическая неизменяемость exact version, fs-verity/kernel immutable seal, специальные retained FD/mmap exclusion proofs и квалификация filesystem машины разработчика не требуются для MVP. Приложение не перезаписывает оригиналы и не переиспользует их ключи; попытки сохраняют собственные ссылки. Late worker output не создаёт Ready/parent/reference/permission. Pending/failed/unknown или неподтверждённые файлы не выдаются. Риск физической перезаписи оригиналов принят владельцем; обработка повреждений/отсутствия файла и отдельные backup/recovery требования сохраняются.
```

Остальные абзацы N3 не заменяются. N5 сохраняется с указанным выше определением completed/verified; в N6 меняется только storage isolation prerequisite. Нормативные notices и append-only запись — фактический diff, сохранённый в [changes.diff](evidence/MVP-PHOTO-READ/changes.diff).

## Реализационная граница

Повторно используются AttachmentService, StoredAttachment, AttachmentScope, LocalFileAttachmentStorage. Новые routes — isolated opt-in чтение списка страниц конкретной submission и оригинала. ComponentAuthorization A1 не расширяется глобально: используются его parent checks и существующий S3 ScopePolicy/IScopedAuthorityReader для каждого attachment. Нет production регистрации, миграций, нового upload/Submit HTTP protocol или automatic legacy qualification. Synthetic fixture создаёт новые фотографии через реальную завершённую запись и существующий claim. Неопределённые исходы записи не повторяются.

Для новых domain writes NR1 durable operation/capacity/recovery protocol остаётся обязательным: исторический AttachmentService не является доказательством всего этого протокола. Здесь он используется для контролируемого fixture upload, без подключения legacy upload HTTP к защищённому контуру. Будущая пользовательская запись требует соответствующего implementation slice; снятие physical seal не закрывает остальные gaps.

Связанный результат: [MVP-PHOTO-READ-RESULTS](MVP-PHOTO-READ-RESULTS.md). Исторические документы не переписаны, effective notices и реестр однозначно задают приоритет. model-domain/implement-use-case/implement-feature skills недоступны; выполнение не заявляется.
