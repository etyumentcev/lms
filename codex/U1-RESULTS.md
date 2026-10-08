# U1 — Attachment Upload & Storage foundation

2026-10-08. Только U1; G Student Solution, History, Teacher Queue/Review не начаты.

## 1. Branch / base / commit

Ветка `codex/u1-attachments`, точная база `4d1400d`. Итоговый commit — commit,
содержащий этот отчёт (`git log -1 --oneline`). Перед работой дерево было чистым.
Baseline выполнен до implementation: 120 frontend / 195 backend tests, обе сборки
без warnings/errors, `docker compose up --build -d --wait` healthy.

## 2. Files changed

- Application: `Attachments.cs` — storage port, upload size configuration, result DTO.
- Infrastructure: `StoredAttachment.cs`, `AttachmentService.cs`, `ImageUploadValidator.cs`,
  `LocalFileAttachmentStorage.cs`, `LmsDbContext.cs`, `PostgresAccessPolicy.cs`,
  `PostgresWorkStore.cs`, новая migration/designer и model snapshot.
- API: `AttachmentsController.cs`, `Program.cs`, `ApiErrors.cs`, `Dockerfile`.
- Web: `components/attachments/{AttachmentUploader.tsx,api.ts,attachments.css}`,
  `api/client.ts`, `App.tsx`, `test/{attachments.test.tsx,attachmentTransport.test.ts}`,
  `scripts/attachments-smoke.mjs`, frontend README.
- Tests: `AttachmentApiTests.cs`, `AttachmentConsistencyTests.cs`, generated
  `AttachmentImages.cs`, real-storage fixture в `ApiFixture.cs`, отдельный ID для
  message photo в существующем `ReadApiTests.cs`.
- Docker: `docker-compose.yml`, `docker/nginx.conf`, `docker/local-auth.js`.
- Documentation: README, ExecPlan, U1 report, Feature/Frontend U1 amendments.

## 3. Attachment lifecycle

Uploaded: immutable original + metadata, authenticated owner, null history references.
Attached: ровно одна Submission или существующая Message; повторная привязка запрещена.
Deleted: технический pending tombstone, никогда не attached. Метаданные сохраняются,
DELETE повторяем для завершения physical removal. Upload не создаёт work/attempt/event.

## 4. Persistence schema / migration

`20261008013249_AttachmentStorage` добавляет только `StoredAttachments` и attachment
triggers. Поля: Id, OwnerId, FileName, MediaType, Size, StorageKey, CreatedAt,
nullable SubmissionId/MessageId, Deleted. FK к AccessAttachments и immutable history,
unique StorageKey, positive size, mutually exclusive references, запрет deleted+attached.
Trigger запрещает изменение original metadata/owner и attached relationship/deletion.
C1 initial migration не изменена. Upgrade действующей F database и clean bootstrap PASS.
Старые исторические ID arrays сохраняются; новые команды требуют реального StoredAttachment.

## 5. Storage abstraction

`IAttachmentStorage.Store/Open/Delete` в Application принимает opaque server key и stream.
В Domain не добавлены binary/path/MIME/HTTP concepts. Filesystem root configuration
находится в Infrastructure, не Application. Порт заменим object-storage adapter без
изменения Domain/Application contracts. S3/Azure/MinIO не добавлены.

## 6. Local implementation

`LocalFileAttachmentStorage`: key — server UUID в N format; любой другой key отклоняется.
Temporary CreateNew → copy/flush → rename без overwrite; original не заменяется.
Ошибки убирают temporary file. Filesystem вне web root/repository, без public static URL.
Storage directory принадлежит non-root API user внутри container.

## 7. Ownership / access

Owner строится только через existing ClaimsActorMapper. Request owner fields отвергаются.
Student читает только своё. Teacher читает собственное pending либо history resource,
для которого existing EnsureCanReview разрешает course/group access. Чужой/неизвестный
GET/DELETE одинаково 404; anonymous 401. Submit foreign/missing/ineligible = 403
AttachmentNotOwned. Metadata-only записи теперь не допускают отправку.

## 8. Upload endpoint

`POST /api/v1/uploads` — сохранён route из Feature §10 / Frontend §8.7. Один multipart
field `file`, один файл, без дополнительных form fields. 201 с attachmentId, fileName,
mediaType, size, createdAt, previewUrl. U1 names заменяют старый recommended response
sketch contentType/sizeBytes; amendment фиксирует это. Physical path/key не выдаётся.
MVC form value providers отключены для action, чтобы лимит/ошибки применялись до чтения.

## 9. Content endpoint

`GET /api/v1/attachments/{id}/content`: protected streaming original, checked media type,
`Cache-Control: private, no-store`, `X-Content-Type-Options: nosniff`. Frontend получает
Blob через общий authenticated client, создаёт object URL и освобождает его при удалении
или unmount. Токен не попадает в URL. Attached история читается после Accept.

## 10. Delete endpoint

`DELETE /api/v1/attachments/{id}`: owner pending → 204. Row lock сериализует его с claim.
Tombstone + ineligible metadata коммитятся до filesystem deletion. Ошибка physical deletion
оставляет недоступный, непригодный для submit resource; повтор DELETE завершает удаление.
Attached → 409 AttachmentAlreadyAttached, без изменения файла/истории.

## 11. Validation / security

JPEG/PNG only. Default 10 MiB, единственный configurable file limit Attachments:MaxBytes;
API transport envelope выводится из него. Nginx не задаёт конкурирующий предел.
Empty/malformed images отвергаются; MIME проверяется вместе с actual signature/structure.
PNG: chunk bounds/CRC, IHDR, bounded decompressed scanlines/filter bytes, end marker.
JPEG: SOI, segment lengths/frame dimensions/components, scan structure, EOI. Не полный
JPEG decoder/forensics. Image area ≤60 million pixels. Ни recompression, ни thumbnail,
ни OCR. Unicode/special/traversal-like filenames остаются только metadata; extension
не определяет формат (настоящий PNG с `.exe` filename безопасно отдаётся как image/png).

Errors: 413 AttachmentTooLarge; 415 UnsupportedAttachmentType; 400 InvalidAttachment
для invalid/empty/malformed multipart; existing generic binding failures ValidationFailed.
403 AttachmentNotOwned, 409 AttachmentAlreadyAttached. Existing Problem Details формат.

## 12. SubmitSolution integration

Existing Application access policy проверяет все IDs, owner, eligibility/expiry, stored
resource и отсутствие claim. AFTER INSERT triggers для Submission/Message блокируют
StoredAttachment rows в стабильном ID order, перепроверяют owner/deleted/claim и связывают
с immutable history. Они работают внутри existing PostgresWorkStore transaction вместе
с work/version/events. Конфликт возвращает existing Problem Details; автоматического retry нет.
Application Submit/Review orchestration и Domain state machine не изменены.

## 13. Ordering

Исходный request array сохраняется как прежний `Submission.AttachmentIds` uuid[] snapshot.
Lock order технический и не меняет page order. Frontend меняет порядок массива до Submit.
Номер страницы — позиция в массиве; filename/time/UUID не используются для сортировки.

## 14. Transaction / crash consistency

PostgreSQL claims/history/work/events атомарны. Конкурентные submits одного файла оставляют
одного победителя; loser полностью откатывается. Delete/submit race не удаляет history file.
Injected failure после history writes откатывает и claim, и work, и journal.

Filesystem + PostgreSQL **не** образуют общую ACID transaction. Upload сохраняет original
до metadata. При DB failure compensating delete выполняется лишь когда DB подтверждает
отсутствие записи; неизвестный commit outcome сохраняет файл. Проверены оба случая.
File failure не создаёт metadata. Crash до metadata может оставить orphan; crash после
tombstone — файл для повторного удаления. Flush/rename полагаются на durability файловой
системы/volume; power-loss directory fsync и distributed commit не заявляются. Backups
должны согласованно включать DB и volume. Runtime не публикует content без metadata.

## 15. Orphan policy

Владелец удаляет pending uploads. Unmount/cancel/потеря HTTP response могут оставить
серверный pending resource; это не domain attempt. Future cleanup после grace period
может чистить старые pending/tombstones/unreferenced files после проверки отсутствия
исторических ссылок. Retention job/scheduler в U1 отсутствует. Uploaded resources сейчас
не истекают автоматически; существующая eligibility expiry policy сохранена.

## 16. Frontend uploader

Reusable AttachmentUploader + attachment API adapter + scoped CSS. Shared Button и
existing ApiClient. Per-page uploading/uploaded/failed, настоящий XHR progress, preview,
remove, retry только failed page, отдельный preview retry, reorder. Successful pages
не исчезают при ошибке другой. Parent callback получает ordered confirmed resources и
readiness; Submit/local draft — responsibility будущего G. Transport 100% не равен success.

## 17. Photo-first / mobile UX

«Добавить фото решения», «Страница N». Native multiple JPEG/PNG input без forced capture:
mobile browser может предложить камеру и галерею. Нет обязательного camera API.
Preview object-fit contain сохраняет страницу целиком. Semantic list, labels/descriptions,
status/error announcements, progress labels, keyboard-native buttons, 44px controls.
Dev/test-only `/dev/attachments` включён Vite dev либо Compose-only host adapter.
Это минимальный harness, без statement, solution tabs, Submit или History.

## 18. Frontend tests

**143 passed = 120 existing + 23 U1**, 0 failed/skipped. Empty/input/mobile/a11y, selection,
success/preview, multiple pages и out-of-order upload completion, labels/order/remove,
failed delete, reorder/keyboard, failure/retry/validation, progress без fake success,
preview retry, unmount cancellation. Transport tests проверяют multipart/auth/progress,
Problem Details + 401 callback, network error и underlying XHR abort. Adapter вызывает
только upload/content/delete; нет ProblemWork creation из uploader.

## 19. Backend integration tests

**30 new cases**, real Testcontainers PostgreSQL + migrated schema + isolated temporary
real filesystem. JPEG/PNG originals; MIME/signature/size/empty/truncation/CRC/multipart;
owner/foreign/anonymous download/delete; pending deletion; real owned Submit, foreign
Submit, immutable claim/order/history, full-course и matching/nonmatching-group teachers;
failed Submit; concurrent claim и delete/submit; metadata-only denial; storage path safety,
no overwrite; DB/file failure compensation; uncertain commit; retry failed physical delete;
rollback after history writes; direct SQL immutability. Test storage directories cleaned.

## 20. Docker volume / configuration

`lms-local_lms-attachments` — отдельный named volume, mounted только в API.
Attachments__Root и Attachments__MaxBytes конфигурируются Compose. PostgreSQL отдельно
хранит metadata. Existing PostgreSQL → migrate → API → Web startup сохранён. API non-root,
без storage container/cloud. API restart и down/up сохраняют original SHA-256. Down -v
удаляет DB + attachment volumes; clean up --build восстанавливает schema/demo.

## 21. Browser smoke

`scripts/attachments-smoke.mjs` с real Compose API/PostgreSQL/storage, без interception:
local login, две фотографии, protected previews, keyboard reorder, pending delete,
anonymous/cross-student denial, zero ProblemWork. 320/375/768/1024/1440 px: no overflow,
images loaded; screenshots сохранены, mobile просмотрен. No page errors. Lifecycle smoke
отдельно доказал API restart, down/up persistence, down -v volume removal и clean bootstrap.

## 22. Backend regression count

**225 passed = 195 existing + 30 new**, 0 failed/skipped:
24 Domain + 15 Application + 10 Infrastructure + 176 API.
Existing 195 cases сохранены. Attachment fixture теперь загружает настоящий файл;
один старый read test использует отдельный upload для Message вместо повторного ID
Submission — все projection/status/history assertions сохранены. Это необходимая
адаптация fixture к принятому U1 single-claim invariant, не ослабление теста.

## 23. Frontend regression count

Исходные 120 D/E/F cases сохранены; дополнительно 19 component/adapter + 4 transport cases.
Всего **143**. Existing Course/Lesson routes и shared shell не изменены по поведению.

## 24. Build results / evidence

Frontend strict TypeScript/Vite: zero warnings/errors. Full solution: zero warnings/errors.
Docker final API/migrate/web build + startup healthy; migrate exit 0. git diff --check PASS.
Transient evidence: `/tmp/lms-u1-baseline-*.log`, `/tmp/lms-u1-{build,tests,web-tests,web-build}.log`,
`/tmp/lms-u1-compose-final.log`, `/tmp/lms-u1-lifecycle.log`,
`/tmp/lms-u1-browser-final.log`, `/tmp/lms-u1-browser/results.json` и viewport screenshots.

## 25. Remaining gaps

U1 blockers отсутствуют. За пределами U1: full Solution/History/Queue/Review UI, persisted
browser draft, cleanup scheduler/retention automation, full image decoder/forensics,
object storage, production OIDC/deployment. Cancellation/unknown upload outcome может
оставить pending orphan; future cleanup policy явно описана выше. Harness не сохраняет
page list после reload и не является будущим Student Solution экраном. Existing read DTOs
сохраняют AttachmentIds; metadata enrichment для будущего G не добавлено.

## 26. ExecPlan changes

§20 фиксирует pre-implementation decisions, route/DTO discrepancy, real-storage boundary,
Message claim protection, lifecycle, compensation/uncertain commit, orphan policy,
baseline/final evidence и exit checklist. Current status обновлён на U1 complete.
Исторические B–F результаты сохранены. **Остановка после U1; G не начат.**
