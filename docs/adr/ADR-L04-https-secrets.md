# ADR-L04 — HTTPS, DNS & secrets

Статус: **PROPOSAL B05**, параметры **DECISION REQUIRED C03/C05/C09**. [Реестр](../../codex/L-DECISION-REGISTER.md), [topology](ADR-L03-vps-deployment.md).

## FACT / ограничения

[nginx](../../docker/nginx.conf) слушает HTTP, выставляет Host/X-Forwarded-For, но не X-Forwarded-Proto; [Program](../../src/Lms.Api/Program.cs) не включает forwarded headers, antiforgery, HTTPS redirection, Identity или явное persistent Data Protection. CORS allow-all не зарегистрирован. Same-origin transport ограничивает URL и redirect; он не доказывает TLS/CSRF readiness. [K security](../../codex/K-SECURITY-AUDIT.md) оставляет deployment policy непроверенной. Текущий dev signing key публичен по назначению, его нельзя перенести как pilot secret.

## PROPOSAL — TLS и доверенная граница

Edge из ADR-L03 единственный принимает public traffic и завершает TLS. Domain A/AAAA должны вести на согласованный сервер; проверять IPv6, чтобы не осталось обхода. ACME renewal и HTTP→HTTPS redirect автоматизированы, но проверяются staging ACME и renewal rehearsal; alerts о failure/expiry. HSTS включать только после рабочего HTTPS/renewal; includeSubDomains/preload не включать без оценки поддоменов владельцем.

API получает forwarded scheme/host/client IP только от известного proxy hop/network, ограниченный ForwardLimit. Edge очищает входные client-supplied forwarding headers и выставляет свои. Настроить AllowedHosts/канонический origin; исключить wildcard trust всех proxies. Middleware до authentication/redirect/cookie generation. [Microsoft proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) описывает восстановление scheme и trusted proxy configuration. Ошибочное доверие ломает Secure cookies, return URLs, IP rate limit; тестировать spoofed X-Forwarded-* извне и прямой private API путь.

E02 ACCEPTED: refresh cookie обязательна; engineering настройки: HttpOnly, Secure Always, Path=/, без Domain, SameSite=Lax и `__Host-` prefix; отдельная antiforgery cookie также Secure, request token только в памяти frontend. Для всех mutations явный antiforgery header; session/CSRF ответы no-store. Login CSRF тоже проверять. Не принимать mutation GET. CORS оставить закрытым для cross-origin; same-origin позволяет не добавлять allow-credentials policy. Origin validation обязательна E02; Fetch Metadata дополнительна, не заменяет CSRF token validation. XSS всё ещё может действовать от пользователя: сохранить Markdown/LaTeX renderer boundaries.

Headers: nosniff, Referrer-Policy без утечки secret URL, frame-ancestors 'none', object-src 'none', base-uri 'self', script-src 'self', connect-src 'self', img-src 'self' blob: (data: только если реально нужно). Это CSP-кандидат: сперва inventory Markdown images/KaTeX styles/fonts/Blob previews/fullscreen и report-only на синтетике; не сломать стили/математику и не добавлять unsafe-inline для scripts ради удобства. Web index и authenticated API не кешировать как public данные; hashed static assets можно кешировать независимо. Не включать публичный CDN для originals.

## PROPOSAL — keys и secrets

Private host directory вне repo, права только нужным service accounts; Compose secrets read-only mounts. Compose сам не шифрует source files на диске. API требует явного чтения secret files/KeyPerFile configuration. Credentials не в images/build args, tracked YAML, shell arguments или logs. Отдельные DB runtime/migration/backup секреты, backup encryption/decryption material, при SMTP его credential; T04 требует JWT signing keys отдельно от HMAC challenge key; algorithm/key rotation/custody подлежат инженерному review, cookie-only mode superseded ADR-L15.

Data Protection: persistent отдельный volume, стабильный ApplicationName для pilot, отдельный namespace/key ring staging. Восстанавливать key ring вместе с конфигурацией; защитить keys at rest явно, например сертификатом с приватным ключом из защищённого secret file. Mount persistent directory сам по себе не гарантирует шифрование ключей ([Microsoft Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)). Backup включает старые keys и возможность расшифровать их; сертификат/decryption secret escrow хранить отдельно от единственного VPS и backup ciphertext. Key rotation не удаляет ключи, нужные retention window; проверять restore после rotation. После disaster restore sessions принудительно отозвать до открытия сервиса (ADR-L05), чтобы старый snapshot не вернул ранее отозванный доступ.

Operator runbook: generate вне Git, deliver ограниченным каналом, record secret identifier/version без значения; test restart/rotation; при компрометации rotate credential + revoke sessions + восстановить доверенную среду. Не публиковать backup ключ в manifest. Proxy ACME state также sensitive и persistent; recovery/перевыпуск зависит от domain control.

## Что можно проверить и что пока нельзя

| Среда | Работа будущего L1/L3 | Gate |
|---|---|---|
| Локальная синтетическая | Cookie/CSRF, proxy spoof tests, production image isolation, key persistence/rotation, test TLS | Разрешение этапа; не реальный сертификат |
| Домен | Canonical host, DNS A/AAAA, ownership, ACME validation, renewal, HSTS scope | C03: владелец домена/доступ к DNS |
| Реальный VPS | Firewall/private ports, permissions, disk, restart, external HTTPS/health, backup egress | C03/C05/C09: ресурс, access, ответственные |
| Владелец | DNS/real secrets/hosting расходы, backup custodians и notification channel | Явные owner decisions, L0 их не закрывает |

Tests: no credentials в bundle/logs/redirect URLs; insecure login не выдаёт usable cookie; external forwarded spoof ignored; HTTPS session survives benign restart; keys missing → readiness fail, не silent regenerate; CSRF wrong/missing rejected для uploads/logout/commands/reset; renewal failure alert доставлен. Точные host/IP/cert paths неизвестны до C03. DNS и сертификаты в L0 не изменялись/не выпускались.


## L0.1 amendment — 2026-10-09 (приоритет для будущих этапов)

Key inventory/custody/rotation/recovery включает Identity Data Protection, backup encryption и отдельный ledger. TLS/шифрование at rest — меры по угрозам; их наличие не доказывает выполнение всех требований к СКЗИ. Нужна применимость PD-G06, а не автоматическая сертификация обычного TLS.

Статус изменений **PROPOSAL**, кроме принятых D4/D5. [ADR-L09](ADR-L09-personal-data-lifecycle.md), [ADR-L10](ADR-L10-erasure-and-immutable-history.md), [lifecycle](../compliance/PD-LIFECYCLE-AND-ERASURE.md), [gates](../../codex/L01-DECISION-GATES.md). **Compliance NOT VERIFIED; pilot NOT_READY.**
