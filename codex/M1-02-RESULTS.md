# M1-02 — защищённый teacher HTTP: актуализация статуса

Дата: 12.10.2026  
Статус: **DONE (component acceptance)**  
Источник реализации и исчерпывающей legacy-матрицы: `codex/TEACHER-REVOCATION-CUTOVER-RESULTS.md` и его evidence.

## Почему статус изменён с PARTIAL

Cutover-отчёт оставлял M1-02 в PARTIAL не из-за падающего teacher workflow, а из-за незакрытой зависимости M1-01, отсутствия обычного browser auth/logout и не квалифицированной записи session audit штатной runtime DB role.

M1-01 теперь закрывает эти условия:

- production React имеет login/refresh/switch/logout без fixture-issued access token;
- enabled host по умолчанию использует restricted DB capabilities;
- `c2h_runtime` выполняет login/session audit через узкую capability без table INSERT, owner или superuser;
- logout отзывает серверную сессию;
- полный финальный API regression: 375 PASS, 0 FAIL, 6 документированных opt-in browser SKIP;
- Frontend: 411 PASS, production build PASS;
- отдельный real Chromium auth E2E: 1 PASS.

## Что не менялось

Teacher Revocation cutover, его защищённые review/message/upload/delete/read routes, transaction/intent semantics и 34-case legacy disposition не переписывались. Этот срез только подключил отсутствовавшую browser session boundary и runtime audit capability.

## Граница результата

DONE здесь означает готовность компонента M1-02 по его собственной карточке и совокупности прежнего cutover evidence + M1-01 evidence. Это не полный пользовательский M1 E2E: fixture-seeded teacher journey остаётся component browser harness, а bootstrap/onboarding/publish/assign относятся к M1-03 и далее.

M1-03 не начат, commit/deploy не выполнялись.
