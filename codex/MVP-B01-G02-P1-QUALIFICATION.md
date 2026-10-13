# MVP-B01/G02 — P1 qualification dossier

2026-10-11. **QUALIFICATION NO-GO · READY FOR OWNER REVIEW.**

## 1. Решение, полномочия и предел исследования

Основание: явное OWNER DECISION текущего задания: closure plan accepted; OD01 A06 deferred, current T04/E03 remain in force; OD02 P1 qualification authorized; OD03 implementation/cutover not authorized. Это решение имеет приоритет над историческими рекомендациями A06. [Closure plan](MVP-B01-G02-CLOSURE-PLAN.md) выполнен в части ограниченной документальной квалификации одного профиля.

**Общий вывод NO-GO:** для профиля P1-SINGLE-LOCAL не получены hard upper bounds qualified clock, PostgreSQL completion и последней controlled HTTP transmission. Каждый из этих gaps отдельно запрещает GO. Дополнительно не доказаны filesystem quiescence, teardown, restart fencing и устойчивый учёт pending capacity. NOT PROVEN означает отсутствие достаточного контракта/oracle, а не доказанную бесконечность каждой операции. NO-GO относится к квалификации профиля, не является новым экспериментальным FAIL компонентов.

Timebox владельца — 3–5 инженерных дней, предельный бюджет, а не обязательное ожидание. Выполнен ранний stop по отсутствующим PG/egress/clock contracts; затрата 3–5 дней не заявляется. Поставщикам сообщения не отправлялись, закрытые договоры не предоставлены, коммерческие гарантии не предполагаются. Новый обзор множества провайдеров, fault experiments и реализация не проводились. Досье не доказывает невозможность любого другого deployment.

Нормативная база: [ADR-L15 T04](../docs/adr/ADR-L15-jwt-delayed-revocation.md), [ADR-L16 E01/E02/O01](../docs/adr/ADR-L16-organization-jwt-browser-session.md), [ADR-L17 E03](../docs/adr/ADR-L17-account-security-recovery-challenges.md), [ADR-L18 E06](../docs/adr/ADR-L18-synthetic-processing-restrictions.md), [owner register](L03-OWNER-DECISION-REGISTER.md), [G02 I1–I7](../docs/architecture/L1-G02-BOUNDED-EXECUTION.md). [Guardrails](SAPIENTUM-MVP-ENGINEERING-GUARDRAILS.md) и [reconciliation](MVP-GUARDRAILS-RECONCILIATION.md) применены. A06 ниже только сравнивается; нормы не синхронизированы и не изменены.

## 2. Один deployment profile: P1-SINGLE-LOCAL

Выбран один конкретный кандидат на существующей основе: self-managed single-host Linux x86_64 Sapientum, один PostgreSQL primary, один ASP.NET Core API, один nginx, локальные private files, существующий compiler worker. Deployment provider — локальный оператор оборудования P03; внешнего managed provider/SLA нет. Это фиксированный объект отрицательной квалификации, **не существующий квалифицированный production deployment**. Версии и topology ниже задают candidate baseline; историческое наличие отдельных компонентов не означает, что вся связка развернута или проверена вместе.

| Слой | Точная версия/конфигурация кандидата | Provenance и ограничение |
|---|---|---|
| Host | Ubuntu 26.04.1 LTS x86_64, Linux 7.0.0-38-generic PREEMPT_DYNAMIC, clocksource tsc | [P03 environment](evidence/L1-G02-A03/environment.json), историческая фиксация 2026-10-09; не новая host attestation |
| Device/FS | WD PC SN5000S SDEQNSJ-1T00-1014 firmware 34230100, /dev/nvme0n1p2, ext4 rw,relatime,stripe=128; private persistent PG/files; worker /work tmpfs | [P03 profile](L1-G02-INFRASTRUCTURE-PROFILE.md); tmpfs probes не доказательство durable NVMe bounds |
| Runtime | Docker 29.8.1 rootless, containerd 2.3.5, runc 1.5.1, rootlesskit 3.1.0, gvisor-tap-vsock, overlayfs | P03 environment; общий host kernel и дополнительные forwarding queues |
| Database | PostgreSQL 17.6 Alpine x86_64; image sha256:ef257d85f76e48da1c64832459b59fcaba1a4dac97bf5d7450c77753542eee94; fsync=on, synchronous_commit=on, full_page_writes=on, wal_sync_method=fdatasync; один primary, без replicas/promotion/2PC | [PG evidence](L1-G02-POSTGRES-EVIDENCE.md); значения зафиксированы в P03, не выданы за инспекцию работающей production DB |
| API | ASP.NET Core runtime 10.0.12; SDK 10.0.401, global.json rollForward=latestPatch; один процесс, port 8080, local private attachments /var/lib/lms/attachments, MaxBytes=10485760 | [Dockerfile](../src/Lms.Api/Dockerfile), [global.json](../global.json), [Compose](../docker-compose.yml). Digest API image и immutable build manifest не представлены: воспроизводимость полного image NOT PROVEN |
| Edge | nginx 1.28.3, OpenSSL 3.5.5 из P03 image sha256:2a9ff2bed2452490f7ac6cc2c64ad6ec8c22aad2ee71d89d7b4705f916fdda14; один HTTP proxy, без CDN/LB/TLS hop; локальный вход 127.0.0.1:8080 | [nginx config](../docker/nginx.conf): proxy_request_buffering off; client_max_body_size 0; absolute deadline отсутствует. Dockerfile nginx:1.28-alpine — плавающий tag; именно P03 image выбран для анализа, его повторное использование не выполнено |
| Clock | chrony 4.8 + Linux monotonic; API сейчас регистрирует TimeProvider.System | [Program](../src/Lms.Api/Program.cs), [QualifiedAuthorityClock](../src/Lms.Application/QualifiedAuthorityClock.cs); production qualified source/lease/dispatch contract отсутствует |
| Compiler | Существующий tools/content_import: Python 3.14.4, systemd 259.5, bubblewrap 0.11.1, Node 24.19.0, KaTeX 0.16.47; PyYAML 6.0.3, markdown-it-py 3.0.0, Pillow 12.1.1 | [C1 platform](evidence/MVP-B07b-C1/platform.json), [C1 report](MVP-B07b-C1-RESULTS.md), [sandbox](../tools/content_import/sandbox.py). Read-only version check: systemctl 259.5-0ubuntu3.4, bwrap 0.11.1; нет запуска worker |
| Compiler caps | wall 30s, CPU 20s, MemoryMax 536870912 bytes, swap 0, TasksMax 32, CPUQuota 100%, TimeoutStopSec 1s; control call timeout 3s, process wait 5s | [Limits](../tools/content_import/model.py). ZIP/expanded по 100000000 bytes, output 200000000 bytes, entries 2000, tmpfs 304194304 bytes. Это per-call limits, не общий admission/orphan cap и не guaranteed termination time |

Точные неизвестные не заполнены вымышленными версиями: полного API image digest, NIC/firmware inventory, immutable distro manifest и production effective configuration нет. Это дополнительные profile-evidence gaps. Не предлагается их компенсировать Compose defaults или заявлять новый provisioning. Development/local-demo конфигурация и credentials Compose не являются deployment security contract и не копируются в новый deployment.

Topology для квалификации: локальный synthetic client → host loopback/rootless forwarding → nginx → API → PG и local file storage. Compiler — существующий отдельный systemd user service/cgroup v2 + bwrap, запускаемый доверенным local caller; HTTP integration отсутствует. Привязка compiler к HTTP не предполагается выполненной и не входит в P1.

**Controlled boundary:** с первого приёма request на host, до последней передачи на host→client socket path включительно; rootless forwarding, nginx, API, kernel queues и retransmit остаются внутри. Client receive после выхода bytes за эту границу не ограничивается. NIC отсутствует в выбранном loopback datapath; перенос на внешний NIC/TLS/LB — изменение профиля, требующее собственного bound/oracle. Даже эта узкая граница не квалифицирована; её нельзя расширить выводом по аналогии.

**Failure domains:** один kernel/scheduler/clock/power domain для API, PG, nginx, systemd и worker; один NVMe/controller для WAL и persistent files; rootless networking — отдельный программный слой, но не независимый fault domain. Отдельного внешнего supervisor, аппаратного fence или независимого time/egress enforcer нет. Process/cgroup isolation защищает от части user-space faults, не от общего kernel/device stall.

**Trusted:** оператор, kernel, systemd, runtime installation, PG/WAL/storage flush semantics, API authorization и nginx; untrusted: client, ZIP/content, slow reader, request metadata. Worker не должен иметь DB credentials, published-write или public-egress capabilities; C1 sandbox evidence ограничено его компонентом. Полная no-bypass карта deployment и immutable seal retained handles не доказаны. Нет public file/static/CDN bypass разрешения.

## 3. Numerical timing ledger

`t0` — earliest controlled ingress, до queue/header/body, как в closure plan и G02 timing proof; `d=t0+B`. Единый budget не перезапускается на API, DB, worker, retry или Range. Новый Range — новый отдельно авторизованный request. Нормативный максимум относится к success/error/cancel/unknown одинаково.

`L=max(last storage activity, PG operation end, worker/handle cleanup, last controlled HTTP transmission)-t0`.

Достаточное консервативное условие: `Q+S+P+X+R+E+J ≤ B`; stages не учитываются дважды. Параллельность требует доказанного dependency graph. Каждый неизвестный член обозначен **NP**, не 0 и не infinity.

| Term | Интервал / accountable component | Ordinary нормативный ceiling | Designated transfer ceiling | Доказанный количественный upper bound |
|---|---|---:|---:|---|
| Q | t0 → queue/header/body/admission complete; nginx/API/host | Внутри 60s | Внутри 480s | NP |
| S | Первая FS operation → последнее завершение I/O/handle; FS/device/API | Внутри 60s | Внутри 480s | NP |
| P | Первая PG operation → commit/rollback/backend release; PG/WAL/device | Внутри 60s | Внутри 480s | NP |
| X | Cutoff detection → dispatch/kill/fence/reap/quiescence; ОС/systemd/operator | Внутри 60s | Внутри 480s | NP |
| R | Authoritative completion evidence, если нужно для stop; PG/controller | Внутри 60s | Внутри 480s | NP; позднее выяснение уже завершённого outcome само по себе не продолжение операции |
| E | Response/error start → last controlled transmission; nginx/rootless/kernel | Внутри 60s | Внутри 480s | NP |
| J | Clock drift/granularity + неучтённый dispatch jitter | Внутри 60s | Внутри 480s | NP |
| Σ | Полный ingress-to-completion, включая teardown | **≤60s** | **≤480s** | **NO-GO / NOT PROVEN для обеих сумм** |

60/480 — максимумы всей операции, не отдельный бюджет каждой строки. Allocation work/cancel/egress невозможно сертифицировать, пока неизвестны tails. `execution_cutoff=d−proven_tail`; proven_tail сейчас NP, поэтому численное безопасное значение cutoff не установлено. Внутренние timeout 30/3/5/1s не суммируются в гарантированный completion bound.

| Число/формула | Статус и смысл |
|---|---|
| 60s | Ordinary ceiling: все обычные API commands/queries, Submit, Review; Ready не long class и остаётся вне реализации P1 |
| 480s | Только явно designated upload/download; наличие файла или compiler не даёт исключение. Без explicit designation применяется 60s |
| 900s | True JWT lifetime максимум; conservative expiry, без positive leeway |
| 600s | `exp ≤ AccessValidUntil−600s`; `Δ+λ+L≤600s` |
| 120s | `600−480`; максимум арифметического остатка для Δ+λ, не дополнительный teardown tail |
| 540s | `600−60`; только арифметика D inequality, не разрешение такой clock error или продления JWT |
| 960s / 1380s | Условные E03 completion ceilings после block: 900+60 ordinary / 900+480 designated; не доказанные SLA |
| 7200s | Ordinary revoke ceiling; T04 issue/revoke linearization и old-token argument сохраняются |
| 0s positive leeway | Требуемая conservative policy, не доказательство настройки всех работающих validators |
| 0.051557492s | Из исторического chrony snapshot: 0.000001050 + 0.003438089 + 0.096236706/2. Условная snapshot uncertainty, **не qualified future bound** |

Clock dossier должен дать UTC interval на каждом decision point, максимальный age, oscillator drift до следующей квалификации, suspend/reboot/step/epoch behavior и worst-case dispatch для уже начатых operations. Chrony formula предполагает правильный stratum-1; здесь нет контракта источника и envelope на весь request. Неизвестный interval → deny issuance/admission; этого недостаточно для уже pending COMMIT/egress. `Δ`, monotonic drift и dispatch остаются NP.

## 4. Vendor/platform guarantees и источник каждого gap

Первичные публичные документы прочитаны 2026-10-11. Они дают семантику API; отсутствие численного promise в проверенных материалах не означает доказательство, что ни один договор в мире его не содержит. Контракт конкретного deployment operator не предоставлен. Исторические A03/A04 измерения не становятся новыми результатами P1.

| ID / supplier | Что доступно | Требуемая внешняя гарантия | Результат |
|---|---|---|---|
| V-PROVIDER / локальный оператор | P03 hardware inventory, без SLA | Bounded scheduling, clock source/holdover, power/storage completion или необратимый bounded fence, independent enforcement/oracles в заявленных faults | Контракт отсутствует; NP |
| V-OS / Linux | [Completion API](https://docs.kernel.org/scheduler/completion.html) допускает uninterruptible waits | Предельное время dispatch, завершения syscall/device I/O, kill/reap, close и queue drain при поддерживаемом отказе | Общего hard bound для этого host не представлено; NP |
| V-PG / PostgreSQL 17 | [Client defaults](https://www.postgresql.org/docs/17/runtime-config-client.html): statement/transaction/lock timers; [signaling](https://www.postgresql.org/docs/17/functions-admin.html#FUNCTIONS-ADMIN-SIGNAL): termination signal/ожидание результата | COMMIT visibility/durability либо доказанный abort/fence до d; rollback/backend release в исходном бюджете | Нет completion bound при WAL/device/scheduler stall; NP. Signal ACK не oracle прекращения |
| V-DURABILITY / PG + device | [WAL reliability](https://www.postgresql.org/docs/17/wal-reliability.html) связывает durability с поведением storage stack | Честный flush, сохранность acknowledged WAL и finite I/O completion/fence | Профиль не имеет device-level proof; NP |
| V-EDGE / nginx | [Core module](https://nginx.org/en/docs/http/ngx_http_core_module.html#send_timeout): send/body timeout относятся к промежуткам I/O | Absolute ingress→last-TX bound, включая forwarding/kernel queues и slow reader; independent cutoff | Нужной гарантии в конфигурации/контракте нет; NP |
| V-CLOCK / chrony 4.8 | [chronyc tracking](https://chrony-project.org/doc/4.8/chronyc.html): условная оценка accuracy через offset/dispersion/delay | Доказанная правильность источника, drift/age/epoch, interval при каждом issue/admission/commit/cutoff | Snapshot не future envelope; qualified production source отсутствует; NP |
| V-API / ASP.NET Core | Source использует CancellationToken, TimeProvider.System; [Program](../src/Lms.Api/Program.cs) | Единый trusted deadline до queue, запрет late stages, bounded tails нижних слоёв | Нет интегрированного G02 proof; NP, cancellation не receipt completion |
| V-FS / ext4 + NVMe | [Storage evidence](L1-G02-STORAGE-EVIDENCE.md), [LocalFileAttachmentStorage](../src/Lms.Infrastructure/LocalFileAttachmentStorage.cs) | Complete read/write/flush/rename/delete/close, immutable exact version при retained FD, quiescence oracle | Нет контракта; NP |
| V-WORKER / systemd+bwrap | C1 caps, cgroup-empty/PID evidence; parent snapshot/cleanup вне worker cgroup | Общий deadline для snapshot→compiler→verify→cleanup; независимый bounded stop | Component limits сохранены, G02 termination NP |

Первоначально открытые PG16/chrony4.5 страницы не использованы для квалификации выбранных версий: сверены PG17 и chrony4.8. Proxy-module URL при проверке вернул ошибку; edge вывод опирается на доступный официальный core-module документ и локальный config/evidence, а не на неполученную страницу.

## 5. Failure assumptions: supported и excluded

**Supported для анализа, не certified timing:** один primary без promotion, локальные private files; непривилегированный клиент, slow/trickle upload/download, disconnect/ACK loss, конкуренция за locks, user-space worker crash/hang, повтор/поздний result. Предполагаются честные trusted components, неизменный runtime installation во время вызова. Даже при исправном host формального worst-case ledger нет: ни один из этих режимов не получает общий timing PASS.

**Обязательные непокрытые faults:** PG/WAL/flush stall; FS D-state и pending device I/O после process exit; scheduler starvation/suspend; edge/rootless/kernel queue stall; clock source loss/step/drift; crash/restart с orphan и неизвестным COMMIT; коррелированный отказ host/control plane. Они остаются gaps, их не исключают задним числом ради GO. Kill того же host/systemd не независимый от этих faults механизм.

**Excluded topology:** HA/promotion/read replicas, второй API host, внешний TLS/LB/CDN, remote/NFS/object storage, внешняя сеть/NIC transport, ARM/Windows/macOS. Это не исследованные альтернативы. Добавление любого из них требует новой qualification. Нет обещания client receipt deadline после выхода bytes из controlled boundary.

**Вне доверенной модели:** Byzantine operator/kernel/DB, dishonest durability и одновременная потеря всех независимых механизмов (которых в кандидате нет). Исключения не разрешают late domain effect и не доказывают containment при обычном crash. Нельзя трактовать отсутствие availability promise как разрешение бесконечного physical tail действующего T04.

## 6. Обязательства, completion oracles и индивидуальные verdicts

Во всех строках ниже bound до `d` означает ≤60s от t0 ordinary и ≤480s от t0 designated; отдельного tail после d нет. Оракулы перечислены как требуемые доказательства: наличие идеи oracle не означает его доступность. Независимость должна переживать соответствующий fault, а не только использовать второй thread/connection.

| ID / нормативный maximum | Ответственный; начало → последняя controlled operation | Quantitative bound / source / assumptions | Независимый oracle и недоказанный сценарий | GO / NO-GO |
|---|---|---|---|---|
| G-CLOCK: JWT≤900s, Δ+λ+L≤600s, L≤B | Clock provider + issuer/API/edge; qualification/issue → последний timing decision и cutoff | Δ/drift/dispatch NP; V-CLOCK/V-OS; trusted reference/epoch | O1: внешний к faulty clock interval/holdover certificate + coherent event timestamps. Synthetic clock tests есть, production O1 нет; suspend/step между admission и COMMIT | NO-GO, NOT PROVEN |
| G-PG: COMMIT/rollback/release≤d | PG/backend/WAL/device; первая DB команда (budget от t0) → visibility-changing COMMIT или доказанный abort и backend cleanup | P/X NP; V-PG/V-DURABILITY; single authoritative history | O2: commit/abort history с qualified time + independent fence/quiescence; fresh serialized observer только после конца writer. COMMIT завис до d и стал виден после d не покрыт; before-commit timestamp не oracle | NO-GO, NOT PROVEN |
| G-IN: queue/header/body/admission входят в B | host/rootless/nginx/API; earliest controlled ingress → body/queue/admission complete | Q NP; V-EDGE/V-API/V-OS; client deadline metadata не trusted | O3: trusted ingress timestamp и queue inventory, correlated independent boundary trace. Медленное body/очередь до middleware; traffic reset timer | NO-GO, NOT PROVEN |
| G-OUT: last controlled TX≤d | nginx/rootless/kernel; request t0, response/error enqueue → последний byte/retransmit на controlled boundary | E/X/J NP; V-EDGE/V-OS; включает все controlled buffers | O4: наблюдение за финальной границей + подтверждённое отсутствие pending TX и bounded independent cutoff. Handler return/close/client read не O4; stalled edge и buffered retransmit не доказаны | NO-GO, NOT PROVEN |
| G-FS: весь I/O/handles≤d | API/worker/FS/device; первый open → последний flush/move/delete/close/device completion | S/X NP; V-FS/V-OS; retained handles включены | O5: backend/device quiescence и exact-version immutable receipt, переживающий заявленный fault. Process exit/rename/chmod/hash недостаточны; поздняя retained-FD write и sync open stall | NO-GO, NOT PROVEN |
| G-WORKER: parent+child+cleanup≤d | local caller/systemd/worker; snapshot start в исходном request → descendants reaped, parent cleanup/handles/I/O завершены | X/S/J NP; 30/3/5/1s только settings; V-WORKER | O6: systemd cgroup/PID identity evidence плюс O5 и независимый supervisor. C1 наблюдает часть O6; parent death/DBus stall/D-state общим oracle не покрыты | NO-GO, NOT PROVEN |
| G-RESTART: no late old effects после d, no resurrected authority | operator/PG/epoch owner; crash/fence initiation → irreversible exclusion old writer + validated authoritative history | Fence latency NP; V-PROVIDER/V-PG; restart не даёт новый deadline. Recovery availability срок не задан | O7: durable epoch/history continuity + fence receipt старого writer. Clean restart A03 не crash/power-loss/fence proof; old process/orphan после рестарта не покрыт | NO-GO, NOT PROVEN |
| G-UNKNOWN: original execution≤d; unknown≠rollback | transaction owner/recovery; отправка COMMIT/потеря ACK → конец original writer; поздний read только выясняет факт | P/X NP; knowledge resolution не имеет отдельного обязательного 60/480s SLA после доказанного stop | O8: serialized OperationId/result на authoritative timeline после O2/O7. Absence пока COMMIT pending не abort; late compensation/replay/purge запрещены. C2-J lost ACK evidence не stop proof | NO-GO, NOT PROVEN |
| G-CAPACITY: finite outstanding, admission до side effects; physical tail≤d | ingress/worker/operator; reserve до effects → реальное release после O2/O5/O6 | Global queue/concurrency/orphan byte limits и drain bound NP; C1 per-call caps не global bound | O9: независимая сверка durable reservations с живыми/pending resources, включая restart. Timeout не освобождает слот; resolver outage + replacements исчерпывают host | NO-GO, NOT PROVEN |
| G-AUTH/E03: expiry≤900s; conditional 960/1380s после block, revoke≤7200s | issuer/transaction owner/API; linearized issue/block/revoke → последний effect/egress | G-CLOCK/G-PG/G-OUT не доказаны; ADR-L15–17; fresh resource/current processing checks отдельно | O1/O2/O4 + atomic block/session/audit history; late credential response/near-exp admission. Component tests не runtime qualification | NO-GO, NOT PROVEN |

Трассировка к I1–I7: I1→G-PG/G-UNKNOWN; I2→G-FS/G-WORKER; I3→G-PG/G-FS/G-AUTH; I4→G-FS/G-RESTART; I5→G-UNKNOWN; I6→весь timing ledger; I7→G-RESTART/G-CAPACITY. Resource scope/current E06, no-public-bypass и immutable history обязательны; P1 не переоценивает их acceptance и не вводит PRS1 exact/H04/B08.

Capacity acceptance требует конечных `N_active`, `N_pending`, `N_orphan`, `bytes_reserved`, queue caps и доказанного enforcement до side effects. Сумма active+pending+orphan входит в лимиты; отдельная неучтённая orphan очередь недопустима. Пока эти числа/ledger отсутствуют, нельзя обещать admission capacity даже если per-worker 512 MiB и 32 tasks соблюдаются. Положительная throughput/availability гарантия не выводится из fail-closed rejection.

## 7. T04/E03 против A06 E03-C + N / B/C

[A06 failure model](L1-G02-A06-FAILURE-MODEL.md), [P1–P9/R0–R4](L1-G02-A06-PROOF-OBLIGATIONS.md) и [exact normative diff ND-01–ND-10](L1-G02-A06-NORMATIVE-DIFF.md) остаются NOT ACCEPTED. Здесь P1 qualification — этап владельца, не одноимённый invariant P1 A06.

| Гарантия | Действующий T04/E03 | Предложенный A06 | Что остаётся доказать |
|---|---|---|---|
| Physical completion | Все исполнители/handles/I/O и egress ≤60/480s | Только доказанно isolated private I/O может оставаться PhysicalPending | T04: full quiescence; A06: P4/P5/P6 isolation/seal/fence вместо universal private-stop. Это реальное ослабление user guarantee |
| HTTP | Последняя controlled передача в B | Тот же B, не client receipt SLA | B-EGRESS, независимый cutoff/oracle; A06 не исправляет nginx/kernel gap |
| Publication | COMMIT и завершение операции внутри B/authority | Visibility-changing p<d и до authority cutoff; late domain publication запрещена | B-PG, atomic result/identity, все readers/counts/claims. Второй COMMIT/private intent сам по себе проблему не решает |
| Unknown | Можно выяснять исход позже только после доказанного окончания исходных исполнителей; не replay/purge | Knowledge и private physical completion могут быть pending независимо | Authoritative resolution, old-writer fence, immutable history, tombstone replay horizon, P7 |
| E03 | Conditional full completion ≤23m после block (ordinary ≤16m при доказанных bounds) | Conditional effects/controlled egress ≤23m; private I/O/knowledge без этого срока | E03-C требует отдельного согласия; clock/PG/egress всё ещё NP |
| Failure model | Supported full-stop обязанность, ограничение failures не тайная отмена T04 | B/C M2 private worker faults допускают isolated pending; M3 PG и M4 edge требуют bounds; M5 restart fence | Correlated-fault independence, capability map, durable accounting и no-late-effect proof |
| Capacity | Нельзя выдавать pending за finished; full stop в B | Pending может длиться, занимая ограниченный ресурс; admission остановится при cap | P9 quotas/reservations/restart ledger, нет обещания bounded recovery availability |
| Auth/security | 60/480/600/900/7200, E01/E02/E06/O01, scope/current restrictions | Числа и эти правила сохраняются | Runtime integration acceptance не следует из нормативного решения |

Изменяемые инварианты и документы при отдельном будущем принятии:

- **I6**: universal physical stop заменяется разделёнными HTTP/publication/private completion условиями; не переносить старый I6/V1 в PASS. **E03 completion**: предел 23m больше не обещает прекращения изолированного private I/O/выяснения outcome.
- **I1/I3** уточняются P1–P3: durable identity, atomic mutation/result/reference, inventory всех visibility consumers и timely publication. **I2/I4** уточняются P4–P6: unique private namespace, immutable seal с retained handles, control fence отдельно от physical quiescence.
- **I5** сохраняет unknown≠rollback, P7 добавляет явный recovery/tombstone contract. **I7** сохраняет запрет старой authority, P6/P9 требуют durable epoch и capacity accounting. **P8** сохраняет fresh read/status/Range authorization. Эти изменения не доказывают реализацию инвариантов.
- ND-01: owner register T04/G02; ND-02–04: ADR-L15 формулы, completion, acceptance; ND-05a–d: L1 security spec; ND-06–07: successor G02 execution/failure model с сохранением истории A02; ND-08–09: S4/S7 scope и acceptance; ND-10/E03-C: E03 register и ADR-L17, связанные ADR-L15 passages. Точные replacements находятся в normative diff, ничего из них P1 не применяет.

**Вердикт по A06 также NO-GO для deployment:** принятие E03-C/N убрало бы universal bound только для доказанно изолированного private I/O. PG, egress, clock, isolation, recovery и capacity blockers останутся. A06 не является минимальным достаточным исправлением всех gaps.

### Полный текст E03-C — предложение, НЕ ПРИНЯТО

> Для согласования E03 с предложенной редакцией T04 отдельно принимаю следующую замену только семантики completion: SecurityBlocked запрещает login/issue/refresh, атомарно отзывает все refresh sessions и фиксирует audit. Unblock — отдельное подтверждённое действие, old grants/sessions не восстанавливаются. Уже linearized access JWT допускает новые admission лишь до true exp≤15 минут. Каждый started request ограничен HTTP≤60s ordinary/≤480s designated upload/download, а новый domain effect/publication — исходным operation deadline и применимым authority cutoff. При доказанных time/publication contracts допустимые новые effects и controlled egress заканчиваются максимум через 23 минуты после block. Это не срок физического окончания доказанно изолированного private I/O и не срок выяснения unknown outcome: они могут оставаться pending, не меняя domain state, eligibility/claim, опубликованные bytes, доступ или egress. Recovery не продолжает expired command. Immediate revoke/mandatory denylist/Redis не вводятся. Current ProcessingRestrictions остаются отдельным требованием. Эта редакция не объявляет соответствующие contracts доказанными и не разрешает реализацию.

### Полный текст N — предложение, НЕ ПРИНЯТО

> После отдельного принятия E03-C принимаю предложенные ND-01–ND-09 вместе с нормативно включёнными A06 FAILURE-MODEL, P1–P9/R0–R4 и S4-SPLIT acceptance criteria как новую редакцию требований. Разрешаю отдельную документационную синхронизацию действующих документов с явной датой вступления в силу и сохранением A02–A04 evidence. 60/480/600/900/7200 и E01/E02/E06/O01 сохраняются. Это решение не квалифицирует deployment, не закрывает proof blockers и не разрешает реализацию S4-A/B.

Оба текста воспроизведены дословно из [A06 owner package](L1-G02-A06-OWNER-DECISION.md). N имеет явную зависимость от отдельного E03-C. Текущая OD01 откладывает их: помещение цитат в dossier не означает acceptance и не разрешает documentation synchronization.

## 8. Минимальный пересмотр и конкретное следующее решение владельца

Рекомендуется принять **отрицательный результат P1 и остановить квалификацию этого профиля**. Увеличение timeout, observed maximum/p99, background cleanup и isolated worker не являются минимальным закрытием T04. Повторять широкий vendor search или начинать gateway по этому dossier нельзя.

При сохранении T04/E03 минимально необходим следующий архитектурный пересмотр области enforcement: внешний к общему host fault domain механизм с контрактами clock/dispatch, PG completion-or-irreversible-fence, FS quiescence и final egress cutoff, плюс независимые oracles. Это необходимые свойства, не разработанная архитектура и не утверждение достаточности какого-либо продукта. Одного дополнительного watchdog недостаточно без bounded device/DB/network semantics. После получения конкретных проверяемых контрактов потребуется отдельное разрешение на пересмотр/квалификацию, затем на реализацию.

Нормативный путь: отдельно рассмотреть E03-C и N, если владелец готов снять физический срок только с изолированного private I/O. **Этого недостаточно для GO данного профиля.** Чтобы оставить именно этот стек без hard PG/egress/clock contracts, потребовалось бы ещё отдельное явное ослабление обещания hard timing для перечисленных platform failures и соответствующих выводов T04/E03. Такой текст не принят, его минимально достаточная редакция здесь не изобретается. Нельзя назвать failure исключённым и при этом обещать неизменный full-completion пользователю.

Точный рекомендуемый следующий owner decision:

> Принимаю MVP-B01/G02-P1 как завершённую документальную квалификацию с общим NO-GO для P1-SINGLE-LOCAL. Gaps G-CLOCK, G-PG и G-OUT являются достаточными stop conditions; остальные gaps сохраняются. T04/E03 остаются без изменений, A06 E03-C/N не принимаю на этом этапе. Новое исследование или архитектурный пересмотр возможно только отдельным решением с конкретным предметом и evidence. Реализация инфраструктуры, fault experiments и HTTP cutover не разрешены. G02 и H04/Ready остаются BLOCKED.

Это подготовленный текст для review, не уже принятое решение. Следующее действие исполнителя — остановка на owner review.

## 9. Проверки документа и сохранность

Canonical `/home/tea/github/lms`; branch `codex/l1-s4-jwt-api-authorization`; HEAD `492e30aef65bbddd24f107903bfaa54a563abe39`. Default main не применялся из-за прямого требования сохранить branch/HEAD/index и dirty checkout. Единственный новый repository artifact этого этапа — этот файл. Checkpoint/history, normative docs и runtime не редактируются.

Preservation baseline: `/tmp/g02-p1-baseline.json`, hashes 551 существующего tracked/untracked файла и index, branch/HEAD. Документационная проверка: local Markdown targets, точность E03-C/N, required obligations/sections, whitespace, branch/HEAD/index и hashes baseline. Финальный результат проверки записан ниже. Baseline снят после чтения, до единственной записи в репозиторий; до него выполнялись только read-only операции.

Guardrails §7 checklist для documentation-only scope:

- covered — actor: владелец/operator; use case: qualification; сущности анализа: request/deadline, operation/transaction, file version, worker/epoch; runtime state transitions не изменяются.
- covered — S2/S3, текущие ограничения и closed gates сохранены; PRS1 exact/H04 implementation/B08 вне scope.
- not applicable — новые server org/course/group/ownership checks: новых commands/queries нет; существующие требования отражены в boundary.
- not applicable — affected subjects/embedded sources: данных субъектов и новых data paths нет.
- not applicable — lists/counts/aggregates: код доступа не меняется; A06 visibility obligation сохранено.
- covered — attachments/history: документация не разрешает bypass, mutation history или purge unknown.
- covered — transaction/locks/CAS/retry/unknown: анализ G-PG/G-UNKNOWN; новых writes и transaction protocol нет.
- covered — validation/limits/error semantics разделены с completion guarantees; NP не PASS.
- NOT RUN — unit/integration/negative-access/E2E, browser и fault tests: documentation-only, runtime не менялся; исторические результаты не присвоены P1.
- covered — schema/API/DI/HTTP/guards/deployment не редактируются; посторонние файлы не входят в change.
- covered — реальные данные/секреты не добавлялись, release/readiness claims не сделаны.
- covered — документальные проверки имеют отдельный результат; runtime proof gaps явно NOT PROVEN.

Специализированные model-domain/implement-use-case/implement-feature skills не доступны в каталоге сессии; их выполнение не заявляется, реализация не выполняется. Commit/merge/rebase/push/deploy и fault experiments не выполнялись.

**G02-P1 DOSSIER READY FOR OWNER REVIEW · A06 NOT ACCEPTED · G02 BLOCKED · H04/READY BLOCKED · S4 HTTP CUTOVER NOT AUTHORIZED.**

Документационная валидация: PASS — local Markdown targets разрешаются; E03-C/N совпадают дословно; whitespace чистый; branch, HEAD и SHA-256 index совпадают с baseline; все 551 baseline файла сохранены byte-for-byte. Runtime/fault tests NOT RUN.
