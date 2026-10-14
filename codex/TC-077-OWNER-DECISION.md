# TC-077 — Owner acceptance decision

Дата: 14.10.2026  
Owner: владелец Sapientum  
Решение: **SAPIENTUM M1 — SYNTHETIC MVP ACCEPTED**

## Основание

- исходный M1-11 rerun3: **74 PASS / 0 FAIL / 3 BLOCKED / 0 NOT RUN**;
- `M1-OD-071`: **WAIVED BY OWNER FOR M1**, без преобразования raw TC-071 в PASS;
- `TC-076 RE-REVIEW: PASS`: независимый Chromium HTTPS E2E, проверка evidence и restore A/B/C;
- текущая owner disposition: **76 PASS / 0 FAIL / 0 BLOCKED / 1 WAIVED BY OWNER FOR M1 / 0 NOT RUN**.

## Решение

Результат этапа M1 принят для использования Sapientum исключительно в изолированной синтетической тестовой среде. Принят полный учебный процесс: администрирование, import/publish курса, назначения, отправка фотографий решений, проверка, NeedsRevision, пересдача, Accepted, история и прогресс.

Разрешён один scoped acceptance commit `test(m1): complete synthetic MVP acceptance` исключительно с документами и evidence завершения M1. Push и deploy настоящим решением не разрешены.

## Обязательные границы

- production deployment **не разрешён**;
- реальные персональные данные детей **не разрешены**;
- `UI-VISUAL-ACCEPTANCE` остаётся обязательной задачей до любого пилота;
- `PRS1 exact = OFF`, `G02 = BLOCKED`, `S4 HTTP NOT AUTHORIZED` сохраняются;
- historical FAIL, raw rerun3 и все исходные evidence не переписываются;
- `PILOT AUTHORIZED` настоящим решением **не объявляется**.

## Подпись решения

Подписант: **владелец Sapientum**  
Способ фиксации: явное owner instruction `OWNER DECISION — TC-077` в задаче от 14.10.2026.  
Итог: **TC-077: PASS — SYNTHETIC MVP ACCEPTED**.
