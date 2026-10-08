# L0.1 — Требования к защите и проверке

[Правовые R](PD-LEGAL-REQUIREMENTS.md): **LEGAL SOURCE NOT VERIFIED**. [Угрозы T](PD-THREAT-MODEL.md). Все C ниже — **PROPOSAL способа исполнения**; обязательность конкретной меры определяется PD-G06 и действующими нормами. Внедрения в L0.1 нет. Owner указан по роли, реальные лица UNKNOWN.

| Control / нормативное требование | Угроза | Мера / компонент | Тест или evidence / owner | Статус |
|---|---|---|---|---|
| C01 R01/R08/R09 | T01/05/10 | ASP.NET Core Identity, уникальный Identity→Actor, closed provisioning, validators/lockout, single-use activation, серверные сессии | Login/recovery/replay/unique mapping; engineer | L1 PROPOSAL, D3 ACCEPTED |
| C02 R02/R08/R09 | T02/03/05 | IAccessPolicy/query/file current scope + независимый purpose/privacy gate перед всеми read/write; права issuer отдельно | Cross-role/group/attachment + запрет blocked при whole-course grant; engineer/issuer | Existing scope FACT, privacy block отсутствует |
| C03 R08/R09 | T09/10 | Session idle/absolute limits, revoke/stamp каждый request, CSRF fetch/XHR, account state reset; timeouts по занятиям/риску | All unsafe methods, revoke/restart/cross-tab; engineer | L1 PROPOSAL |
| C04 R08/R09 | T01/06/14 | Privileged MFA рекомендована; user MFA по угрозам/usability; ограниченный SSH, не shared admin, password policy/восстановление | Access review, MFA recovery/rotation/drill; ИБ/ops | PD-G06, не blanket legal MFA |
| C05 R05/R08/R09 | T04/06 | HTTPS same-origin, trusted proxy, private DB/files/network, egress allowlist | Ports/TLS/header spoof/static file denial, договор ЦОД; ops | L3 PROPOSAL |
| C06 R08/R09/R20 | T06/07 | Backup encryption; disk/DB encryption at rest по угрозам и средствам защиты; ключи отдельно, least privilege, rotate/recovery | Key inventory, separate escrow, rotation и missing-key test; ИБ/ops | PROPOSAL; crypto compliance отдельно |
| C07 R08/R09/R17 | T08/14 | Security audit вне DomainEvents, route template/outcome, без bodies/tokens, bounded retention, защищённый sink/time sync | Canary scan, tamper/access/expiry tests; ИБ | K traceId FACT, полноценный audit отсутствует |
| C08 R08/R09 | T11/16 | API/edge quotas/rate/concurrency, upload validation, alarm disk/inodes/WAL, безопасные images | K per-file baseline + concurrent/ENOSPC/load; engineer/ops | Per-file FACT; K-S02 OPEN |
| C09 R08/R09 | T06/15 | Inventory OS/images/dependencies, vulnerability review, patch schedule с emergency path, staging/rollback, secret rotation | CVE triage по сроку риска, patch/restart/secret canary; ops | L3 PROPOSAL, сроки PD-G06/13 |
| C10 R10–R13 | T12/18 | Case registry/deadlines, purpose block, privileged erasure manifest, physical deletion и подтверждения | DSAR/withdrawal/erasure races/crash/failure evidence; ПД/engineer | L1/L2 PROPOSAL |
| C11 R11–R13/R08 | T07/13 | Coordinated encrypted backup, separate current ledger, quarantine restore/reconcile без public access | Snapshot-before-erasure drill; missing ledger fails closed; ops | L3 PROPOSAL, K-S06 OPEN |
| C12 R15/R16 | T06/07/08/16 | Incident runbook, ответственный/заместитель, таймеры, защищённая forensic копия с отдельным retention | Synthetic notification/tabletop, delivery acknowledgment; incident lead | L3/L4 NOT VERIFIED |
| C13 R01/R18/R19 | T04/17 | Minimal field allowlist/EXIF decision, no trackers/AI, review anonymization | Schema+network+content checks, reidentification assessment; ПД | PD-G04/05 OPEN |
| C14 R02/R07/R14/R17 | T05/14/18 | Signed policy/orders/contracts/training, notifications, access reviews | Подписи, квитанции, provider evidence, legal memo; директор/ПД | Не реализуется одним кодом |

App runtime не получает DDL/superuser/erasure capability. Отдельный deploy/migration principal и отдельный временный maintenance principal — проект L3. Password hash не шифруется «обратно»; TLS и disk encryption защищают разные угрозы. Secrets, Data Protection keys, backup encryption keys и HMAC ключ ledger имеют разные назначения и отдельные lifecycle/custody.

Периодичность patch, access review, control assessment и ротации определить по актуальному нормативному минимуму и модели угроз, не произвольно назвать defaults достаточными. Incident response включает временное закрытие доступа, сохранение ограниченных доказательств, оценку вреда, уведомления, устранение причин и проверку восстановления. [Lifecycle](PD-LIFECYCLE-AND-ERASURE.md) отделяет блокирование обработки от account disable.
