# L0.1 — Compliance matrix и release gate

Все нормативные R берутся из [реестра с точными кандидатами пунктов и дат](PD-LEGAL-REQUIREMENTS.md): **LEGAL SOURCE NOT VERIFIED**. Норма/применимость и реализация — отдельные оси; ни одна строка не объявляет юридический PASS. Инвентарь I — [данные](PD-DATA-INVENTORY.md), B — [основания](PD-PROCESSING-BASES.md), C — [меры](PD-SECURITY-CONTROLS.md), F — [потоки](PD-DATA-FLOW-MAP.md), T — [угрозы](PD-THREAT-MODEL.md).

| Норма | Applicability | Компонент/процесс | Способ исполнения (PROPOSAL) | Проверяемое evidence | Owner | Gate / этап | Статус |
|---|---|---|---|---|---|---|---|
| R01 | I01–I15 / B01–B12 | Basis register/field allowlist/retention schedule | C13/C10; утверждение целей и схемы | Legal memo + field review + retention job negative tests | ПД/юрист/engineer | PD-G02/04/07; L1/L2 | NOT VERIFIED / OPEN |
| R02 | VPS/admin/backup/teacher contractors | Vendor/role register, договоры/поручения | C14/C04; current scoped access | Signed поручения + provider access/deletion evidence | Директор/юрист | PD-G05/12; L3/L4 | NOT VERIFIED / OPEN |
| R03 | Consent-based цели | Versioned consent/evidence/withdrawal | ADR-L09; separate text/action | Текст версии, подтверждение/отзыв, replay tests | ПД/engineer | PD-G02/03; L1 | NOT VERIFIED / OPEN |
| R04 | Дети/представители | Controlled onboarding/representative proof | B01/B03; минимальная проверка полномочий | Age-category legal memo + valid/invalid representative tests | Юрист/issuer | PD-G03; L1/L4 | NOT VERIFIED / OPEN |
| R05 | Все граждане РФ/сбор | F01–F08 + infrastructure inventory | C05/C06; РФ для DB/files/keys/copies/logs | ЦОД contracts + egress/network/config evidence | Ops/ПД | PD-G05; L3/L4 | NOT VERIFIED / OPEN |
| R06 | При иностранных получателях | Transfer review/notification | C13; отключение flow до разрешения | Recipient assessment/official notice/decisions или доказанное N/A | ПД/юрист | PD-G05/11; L3/L4 | NOT VERIFIED / OPEN |
| R07 | ООО | Политика/приказы/обучение/контроль | C14; публичная версия и допуски | Подписи, публикация, ознакомления, review reports | Директор/ПД | PD-G01/10/12; L4 | NOT VERIFIED / OPEN |
| R08 | Вся ИСПДн | Threat/classification + architecture | C01–C12 по уровню | Акт уровня/модель/ведомость мер/эффективность до ввода | ИБ/ПД | PD-G06; L1–L4 | NOT VERIFIED / OPEN |
| R09 | По действующему акту ФСТЭК | Базовый набор/адаптация/компенсации | Control applicability в PD-SECURITY-CONTROLS | Официальная редакция + построчная ведомость + tests | ИБ | PD-G06/14; L3/L4 | NOT VERIFIED / OPEN |
| R10 | Запрос/уточнение | Case/deadline/export | C10; verified scope/secure response | Synthetic access/correction/deadline + evidence delivery | ПД/engineer | PD-G08/10/12; L1/L4 | NOT VERIFIED / OPEN |
| R11 | Прекращение/отзыв/незаконность | Purpose block/erasure graph | C10/C11; ADR-L10 | Read/write deny + manifest/deletion/copies/reasoned retention | ПД/ops | PD-G07/08/09; L1–L4 | NOT VERIFIED / OPEN |
| R12 | Требование/невозможность уничтожения | Deadline/block/exception register | C10/C11; отдельное legal decision, не forever backup | Таймер, мотивированное исключение, retry/closure | ПД/юрист | PD-G07/08/09; L2–L4 | NOT VERIFIED / OPEN |
| R13 | Уничтожение | Акт + log export/provider confirmation | C10; controlled manifest verification | Полные поля/подписи/срок evidence, проверка replicas | ПД/reviewer | PD-G08/09/12; L2–L4 | NOT VERIFIED / OPEN |
| R14 | Начало/изменение/прекращение | RKN register change checklist | C14; appointed signatory/calendar | Квитанция/реестр/актуальная форма, исключения по фактам | ПД/юрист | PD-G11; L4 до начала | NOT VERIFIED / OPEN |
| R15 | Инцидент по законному триггеру | Incident runbook/notifications | C12; два таймера от выявления | Tabletop, contact/24h/72h draft и receipt при событии | Incident lead/ПД | PD-G13; L3/L4 | NOT VERIFIED / OPEN |
| R16 | Компьютерный инцидент | Route РКН/НКЦКИ по статусу | C12; отдельная applicability assessment | Актуальный ФСБ акт/канал, drill | ИБ/юрист | PD-G11/13; L3/L4 | NOT VERIFIED / OPEN |
| R17 | Оценка вреда | Акт вреда детям/другим субъектам | C14 + threat model | Утверждённая оценка/соразмерность мер | ПД/ИБ | PD-G06; L4 | NOT VERIFIED / OPEN |
| R18 | Специальные/биометрия/распространение | Content/field/flow review | C13; запрет public photo/биометрической цели | Purpose review + incidental-content process, N/A не по формату файла | ПД/юрист | PD-G04; L1/L4 | NOT VERIFIED / OPEN |
| R19 | Обезличивание или специальный госрежим | ADR-L10 вариант B только при evidence | C13; aggregate privacy assessment | Действующая методика/невосстановимость связи/отдельное основание | ПД/ИБ/юрист | PD-G08/14; L2/L4 | NOT VERIFIED / OPEN |
| R20 | AI decision либо СКЗИ | Future integration / crypto review | C06/C13; отдельный ADR | Applicability memo; AI disabled; СКЗИ evidence при требовании | ИБ/юрист | PD-G06/14; L3/L4 | NOT VERIFIED / OPEN |

## Что является FACT, а что ещё предстоит

K historical evidence: scoped authorization, file validation/parent access, immutable triggers, six-screen workflow, 366 frontend/260 backend. Это не проверка Identity, consent, erasure, deployed localization или документов оператора. K-S02/S05/S06 и deployment/device gates сохраняются. Подробности [K security](../../codex/K-SECURITY-AUDIT.md), [K readiness](../../codex/K-RELEASE-READINESS.md). L0.1 не перезапускал app tests и не создавал новые PASS реализации.

Условная норма может стать NOT APPLICABLE только с фактами, официальной нормой, мотивировкой, owner/date и review при изменении flow. Например, «нет биометрической цели» не отменяет обычную защиту фото, «нет иностранного provider» не доказывает отсутствие зарубежного support/SMTP. Рекомендации безопасности отдельно выбираются по PD-G06, не получают статус обязательного требования без основания.

## Acceptance packet до первого реального пилота

1. Официальные редакции/даты/применимость всех R подтверждены юристом; PD-G01–14 закрыты evidence либо мотивированной неприменимостью условного пункта. D4/D5 неизменны.
2. L1 Identity, scope, basis/representative/session/privacy block/request paths проверены на synthetic; L2 retention/erasure/гонки/акт работают; L3 localization/keys/backup/quarantine restore/incident drills подтверждены.
3. Политика опубликована, документы подписаны, лица назначены/обучены, подрядчики проверены; уведомления исполнены до начала обработки. Доступность реального request channel проверена.
4. L4 functional/security/lifecycle E2E, реальные выбранные устройства и полная матрица мер выбранного уровня имеют date/commit/environment/tester/результат/артефакт; все K обязательные blockers закрыты.
5. Директор принимает scope и отдельный запуск. Успешный doc-check, подписанный ADR или разрешение L1 сами по себе не дают READY_FOR_PILOT.

Любой обязательный gap → **NOT_READY**. Юридическое согласование без технических доказательств и зелёные тесты без организационных документов одинаково недостаточны. **Compliance status: NOT VERIFIED. Pilot readiness: NOT_READY.**
