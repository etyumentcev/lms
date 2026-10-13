# ADR-L03 — VPS & Docker Compose

Статус: D1 собственный VPS/сервер и D2 Docker Compose **ACCEPTED**; topology ниже **PROPOSAL B04**, параметры **UNKNOWN / C03, C06**. [Реестр](../../codex/L-DECISION-REGISTER.md).

## FACT — существующая система

[docker-compose.yml](../../docker-compose.yml): postgres 17.6-alpine → one-shot migrate (`--migrate-only`) → API → nginx Web. Только Web публикует 127.0.0.1:8080:80; API/DB без host ports, одна default network. Два named volume: postgres-data и lms-attachments. Postgres readiness через pg_isready, API `/api/health` проверяет DB CanConnect, Web `/health` только nginx. `restart: no` указан лишь migrate; остальные restart policies не заданы. Healthcheck сам по себе не перезапускает unhealthy container.

Compose включает Development, публичные DB credentials/signing key и LocalDemo.Enabled. Это local stack, не pilot template. [API image](../../src/Lms.Api/Dockerfile) работает под APP_UID; [Web image](../../src/Lms.Web/Dockerfile) имеет отдельный production target. [nginx.conf](../../docker/nginx.conf) same-origin proxy `/api/`, SPA fallback, без TLS, body size unlimited на nginx (ограничивает API). [Program](../../src/Lms.Api/Program.cs) мигрирует только по флагу, автоматически на обычном startup не мигрирует; seed включается лишь в dev mode. Переиспользовать эту последовательность, не seed.

## PROPOSAL — один сервер, одна API replica

```text
Internet HTTPS :443 / HTTP :80 redirect+ACME
    -> TLS reverse proxy
        -> /api/* -> API :8080 -> PostgreSQL :5432
        -> /*     -> Web production nginx :80
API -> private attachments volume + Data Protection key volume
Operator one-shot migrate/provision/backup -> private DB/storage
Encrypted completed backup -> off-host owner-selected storage
```

Предпочтительно Caddy edge для automatic TLS, существующий nginx остаётся static Web. Направить /api прямо в API, исключить лишний proxy hop и неоднозначность X-Forwarded-*; pilot nginx internal API route не должен создавать обход limits. Альтернатива: существующий nginx как единственный edge + ACME client/timer (меньше web containers, больше renewal glue). Traefik service discovery не нужен для фиксированных сервисов. Выбор инструмента технический B04, D1/D2 не пересматриваются. TLS automation у Caddy — [официальная документация](https://caddyserver.com/docs/automatic-https); перед L3 проверить pinned version/config.

Сети: edge↔Web/API frontend network; API/migrate↔Postgres private backend network, PostgreSQL только на ней. Web не получает DB network/credentials. Proxy не монтирует originals; originals выдаёт только authorized API. API egress не перекрывать слепо, если C02 позже потребует SMTP; точный egress allowlist после выбора. Публично только 80/443; SSH ограничен operator source/VPN/firewall отдельно от web. Не выставлять 5432/8080 и Docker socket.

Volumes: DB, attachments, Data Protection keys, proxy ACME state. Backup staging — отдельное ограниченное место, чтобы не заполнить рабочий диск; права API только на свои files/keys. Read-only rootfs/tmpfs там, где проверено совместимо; resource limits определять измерением. Secrets из mounted read-only файлов, private host directory вне Git, app configuration adapter читает их явно (Compose secret mount не превращается автоматически в IConfiguration). Отдельные DB runtime/migration/backup credentials с минимальными правами, проверенные против EF/trigger функций.

Long-lived services: предложить `unless-stopped`; migrate/provision/backup one-shot без бесконечного restart. Health: reuse DB/API checks + internal storage write/read probe и key availability, HTTP external synthetic check без student data. Не раскрывать paths/config в health response. Restore/maintenance marker делает readiness отрицательной и не допускает трафик. Crash restart и unhealthy alert — отдельные тесты.

## PROPOSAL — deploy/migrate/recovery workflow L3

1. Build/test immutable images с commit/digest, dependency inventory; inspect production Web на отсутствие local-login/local-auth. Проверить Production, demo disabled, secrets present; staging preflight запрещает public dev key/config.
2. Dry-run миграций на восстановленной копии; оценить compatibility и окно. Включить maintenance, drain API и фоновые writers. Получить проверенный согласованный backup (ADR-L05).
3. Один migrate job с отдельным migration credential и DB lock, никакого параллельного startup migrate; сохранить список migration IDs и redacted log. При ошибке держать maintenance, app не стартовать с несовместимой схемой.
4. Поднять API/Web, проверить health, login/scope/CSRF/private download и smoke на отдельном синтетическом наборе; только затем снять maintenance. Seed real dataset отдельной approved provisioning процедурой.
5. App rollback к предыдущему digest только если матрица schema compatibility это допускает. Иначе forward fix либо согласованный restore DB+files+keys одного backup set. Не применять Down миграцию вслепую и не использовать down -v на pilot.
6. Release manifest: commit/digests, config revision без секретов, migration IDs, backup id, operator/date, checks/result. В инциденте учитывать потерю данных после backup, не обещать RPO без измерений.

Logs stdout/stderr с bounded rotation; no passwords/cookies/reset codes/bodies/photos. traceId связывает app errors; proxy log scrub query и headers. Audit provisioning/revoke отдельно с доступом оператора. Alerts: availability, DB/storage capacity, backup age/failure, TLS expiry, elevated 5xx/429. Получатель/время реакции — C09, не выдуманный SLA.

## Capacity: UNKNOWN и способ расчёта

[K performance](../../codex/K-PERFORMANCE-REPORT.md) — один последовательный local caller; API 227.8 MiB и DB 40.7 MiB после прогона **не peak memory**. Queue page50 p95 39.28 ms/5 SQL, P5 teacher 59.36 ms; Lesson1001: 2011 queries, dense canvas p95 next-frame73ms. Ни одно число не задаёт достаточный VPS размер.

C06: число учеников/учителей, одновременность, задания/неделю, попытки/задание, фото/попытку, реальные размеры файлов, срок хранения, размер урока. Оценка originals = students × assignments × attempts × photos × measured bytes, плюс teacher messages/pending. Disk = originals + DB/index/WAL + временные uploads/reservations + backup staging + logs/images + обоснованный free reserve. Off-host retention отдельно. RAM измерить как peak API (multipart + MemoryStream + ToArray + image validation) + PostgreSQL + proxy/OS при mix нагрузки, backup и restore. CPU/IO: p95/p99, saturation, DB pool wait, swap/OOM, disk throughput на candidate host. Увеличивать concurrency до agreed budgets, выбирать запас по measured burst. Не выдавать модель процессора K за рекомендованный VPS.

Exit L3/L4: только public edge reachable; secrets redacted; restart сохраняет originals/keys; migration failure не открывает partial deployment; rollback drill и clean-host restore доказаны; capacity соответствует C06. C03 предоставляет domain/server/OS/operator access, C04–C05 backup, C09 ops. Пока UNKNOWN, нельзя утверждать защищённое pilot deployment.


## L0.1 amendment — 2026-10-09 (приоритет для будущих этапов)

Контур real-data pilot выбирается с PD-G05/06: все DB/files/Identity/logs/backups/support/egress проверяются на размещение и передачи. D4 ООО РФ и D5 compliance до первого пилота приняты; local Compose не evidence локализации.

Статус изменений **PROPOSAL**, кроме принятых D4/D5. [ADR-L09](ADR-L09-personal-data-lifecycle.md), [ADR-L10](ADR-L10-erasure-and-immutable-history.md), [lifecycle](../compliance/PD-LIFECYCLE-AND-ERASURE.md), [gates](../../codex/L01-DECISION-GATES.md). **Compliance NOT VERIFIED; pilot NOT_READY.**
