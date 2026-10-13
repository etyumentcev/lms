# G02/A03 — Edge evidence

2026-10-09. **INFRASTRUCTURE EVIDENCE BLOCKED · T04 NOT PROVEN · S4 BLOCKED.**

## Полный путь и точка завершения

Целевой путь: earliest controlled ingress admission → queue/body → proxy → API → PG/storage → API stream → proxy userspace/temp buffers → kernel TCP send/retransmit queues → forwarding/vNIC/qdisc/NIC → последний контролируемый egress. t0 фиксируется **до очереди/body**, активный трафик его не сдвигает. Каждый download/Range — отдельная вновь авторизованная операция; Range не продлевает старый deadline. Текущий controller не объявляется новым Range implementation.

**Last-byte completion** — время последней фактической передачи любых байтов этой операции из последнего контролируемого egress, включая ранее поставленные в очередь байты/повторные передачи на этой границе. Handler return, write(), close(), nginx process exit и client EOF — разные наблюдения. Нужно завершить/сбросить все соответствующие очереди либо иметь доказанный drain bound. Момент получения уже вышедших bytes клиентом во внешней сети не ограничивается T04 моделью A02.

Probe path: loopback client → Docker rootless forwarding → nginx 1.28.3 → loopback nc synthetic upstream. Это не end-to-end LMS/API/TLS/NIC испытание. Физический NIC не участвовал как инструментированный финальный egress; rootless forwarding добавляет queue. Независимого absolute cutoff gate за границей nginx/kernel нет.

## Контракты и измерения

[nginx proxy module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html) различает buffering и read/send inactivity timers. [Core send_timeout](https://nginx.org/en/docs/http/ngx_http_core_module.html#send_timeout) относится к интервалу между write operations. Наличие активности допускает суммарную длительность больше timer. Отключить buffering полезно для уменьшения очереди, но это не absolute whole-response deadline.

[Linux TCP](https://man7.org/linux/man-pages/man7/tcp.7.html) описывает socket buffering/retransmission; настройки TCP не представлены как аппаратная deadline гарантия. [close](https://man7.org/linux/man-pages/man2/close.2.html) не является end-to-end delivery/quiescence oracle. FIN/RST/linger требуют отдельного исследования для выбранной сетевой границы; ни один не объявлен способным вернуть уже вышедшие bytes.

| Проверка | Реальный результат / oracle | Ограничение |
|---|---|---|
| Trickle response; buffering off; proxy_read_timeout/send_timeout=1s | Все 10 bytes дошли за интервал >1s; client monotonic elapsed/HTTP body | COUNTEREXAMPLE к absolute timeout; это seconds-scale, не 480s test |
| Buffering on; client не читает 300ms; upstream 128KiB; docker kill nginx | После observed container stop клиент получил queued response bytes; trace фиксирует количество и время | Не локализует, были ли bytes уже в receive buffer/forwarding queue; не доказывает поздний физический egress |
| Kernel socket buffers/close | TCP sender отправил 65536 bytes и закрыл socket; receiver начал читать позже и получил все bytes | Часть/все bytes могли пересечь controlled boundary до close; доказывает только неэквивалентность close и client completion |
| API streaming / slow upload / every Range / intermediary proxies | Source review baseline; новая интеграция NOT RUN | Нужно общий budget ingress-to-egress, stop upstream workers/backend и body/handle cleanup |
| Зависший edge/kernel, qdisc/NIC pending bytes, retransmission cutoff | NOT RUN, нет hardware observability/fence contract | E/X/J NOT ESTABLISHED; containers не independent edge supervisor |

Исходный [nginx config](../docker/nginx.conf) отключает request buffering и не задаёт absolute operation deadline. A03 его не меняет. Default response buffering может продолжать выдачу после upstream return. Для будущего boundary требуется контролировать все proxy hops, запретить direct/public storage/CDN bypass и доказать cutoff queued egress; downstream outside control не обещать bounded receive.

## Вывод

Измерен ограниченный counterexample; **absolute controlled-egress bound отсутствует**. Закрытие delivery безопасно как реакция на unknown, но не доказательство I6. Нужен отдельно проверяемый independent gate/queue fence с контрактом, охватывающим kernel/runtime/NIC; параметры nginx inactivity не закрывают этот blocker. [Timing proof](L1-G02-TIMING-PROOF.md) сохраняет E как неизвестный, а не подставляет наблюдённые миллисекунды.
