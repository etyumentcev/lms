# M1 Synthetic MVP acceptance matrix — rerun 3

Дата: 14.10.2026. Baseline: `main@688d8cf3305d7739a3cb76824a60ad9d5de3596b` плюс перечисленные ниже изменения только acceptance tests/harness/docs. Исходный результат rerun 3 сохранён без изменения: **74 PASS / 0 FAIL / 3 BLOCKED / 0 NOT RUN**. Текущая owner disposition: **76 PASS / 0 FAIL / 0 BLOCKED / 1 WAIVED BY OWNER FOR M1 / 0 NOT RUN**. Решение `M1-OD-077`: **SYNTHETIC MVP ACCEPTED**.

## Evidence legend

- **J** — `codex/evidence/M1-11/rerun3-688d8cf/complete-6/`: clean PostgreSQL 17.6, HTTPS API, production React, private storage, Chromium, E01–E15 journey, exact 12×5 aggregates, restart and restore.
- **G** — `codex/evidence/M1-11/rerun3-688d8cf/browser-gaps/`: исходные и исправленные `/my/courses` и real-auth teacher harness runs.
- **R** — `codex/evidence/M1-11/rerun3-688d8cf/regression/`: .NET, Chromium, ProcessingContext, Vitest and build evidence; compiler/importer console results recorded in `M1-11-RESULTS.md`.
- **U** — J screenshots at 390/768/1440. Все 11 exact approved PNG filenames отсутствуют; визуальный verdict только `REFERENCE UNAVAILABLE`.
- **P** — preflight: нормативные SHA-256, owner decisions, AGENTS.md, branch/HEAD/status and preserved dirty files.
- **W** — отдельное решение [M1-OD-071](L-DECISION-REGISTER.md#m1-acceptance-extension--2026-10-14): `WAIVED BY OWNER FOR M1`; не является `PASS` и не меняет исходный evidence.
- **I** — независимый [TC-076 re-review](TC-076-INDEPENDENT-RE-REVIEW-RESULTS.md): fresh normal-auth Chromium HTTPS lifecycle, evidence audit и restore A/B/C, PASS.
- **O** — [TC-077 owner decision](TC-077-OWNER-DECISION.md): synthetic-only acceptance; не pilot/production authorization.

`PASS` не перенесён из FIX-01/FIX-02. `BLOCKED` означает внешний acceptance gate, который этот исполнитель не вправе закрыть. TC-071 хранит исходный blocked/reference verdict; W — отдельная owner disposition только для обязательности M1.

| ID | Тест | Статус | Фактический результат / evidence / дефект |
|---|---|---:|---|
| TC-001 | Чистый запуск на пустой PostgreSQL | **PASS** | J: новый PostgreSQL, все migrations, новый private root, HTTPS/React health PASS. |
| TC-002 | Создание организации A системным оператором | **PASS** | J: Org A/B созданы через React bootstrap; `screenshots/E01-bootstrap-1440.png`. |
| TC-003 | Первоначальная активация администратора | **PASS** | J: одноразовый code → password → normal login; `E01-admin-1440.png`. |
| TC-004 | Изоляция системного и организационного администратора | **PASS** | J+R: platform credential не принят component routes; org isolation negatives PASS. |
| TC-005 | Валидация и пустые admin states | **PASS** | J+R: empty/no-authority and validation states exercised; browser/admin suites PASS. |
| TC-006 | 12 учеников и два преподавателя | **PASS** | J: P01–P12, T-A, T-X созданы UI; DB oracle 14 participant profiles. |
| TC-007 | Одноразовая активация и первый вход | **PASS** | J: все participants активированы и вошли через React; 390 screenshots. |
| TC-008 | Неверный/просроченный/повторный/blocked code | **PASS** | R: AccountChallenge negative matrix PASS. |
| TC-009 | Recovery с независимым подтверждением | **PASS** | R: approval, session revocation and linked identity tests PASS. |
| TC-010 | Одна identity в двух организациях | **PASS** | R+J: shared identity/session isolation PASS; две org в J. |
| TC-011 | Login, reload, expired JWT refresh | **PASS** | G: `my-courses-fixed-5.trx`, normal React login/reload/expiry recovery 1/1. |
| TC-012 | Logout в двух вкладках | **PASS** | G: two-tab cross-tab logout and cleared client state PASS. |
| TC-013 | Refresh replay, CSRF, foreign Origin | **PASS** | G+R: 409 replay, 403 missing CSRF/foreign Origin PASS. |
| TC-014 | Draft course и G1/G2 | **PASS** | J: React admin creates draft and both groups. |
| TC-015 | 12 enrollments и teacher assignment | **PASS** | J: 12→G1; T-A→G1; T-X→G2; CourseEditor separate. |
| TC-016 | Replay/CAS/foreign assignment denial | **PASS** | R: roster idempotency, stale CAS and cross-org denial PASS. |
| TC-017 | Move с immutable history | **PASS** | R: move preserves assignment group/version and history. |
| TC-018 | Remove/revoke | **PASS** | G+R: fresh session after revoke has no scope; data remains immutable. |
| TC-019 | Valid ZIP import | **PASS** | J: Ready diagnostics and published content from real ZIP. |
| TC-020 | Invalid ZIP diagnostics | **PASS** | J: deterministic file/line diagnostic, then valid import. |
| TC-021 | Student/teacher preview separation | **PASS** | J: both previews; teacher-only canary absent from student HTML/API. |
| TC-022 | Immutable v1 publish | **PASS** | J: published v1, DB/API consistency. |
| TC-023 | Open lesson + five G1 tasks | **PASS** | J: 60 exact-version assignments; G2 excluded. |
| TC-024 | v2 does not rewrite v1 | **PASS** | J: v2 published; all prior assignments remain v1. |
| TC-025 | Concurrent publish/lost response | **PASS** | R: single winner/idempotent receipt/authoritative read tests PASS. |
| TC-026 | Student course and lesson | **PASS** | J: course/lesson real React at 1440/768/390. |
| TC-027 | Problem and tabs | **PASS** | J: condition, Solution/Hints/History navigation PASS. |
| TC-028 | Exact student hints projection | **PASS** | J: hint present; teacher-only material absent. |
| TC-029 | Protected exact-version reference | **PASS** | J: teacher sees v1 solution/rubric/notes after v2; unauthorized denial PASS. |
| TC-030 | No teacher material in student cache/assets | **PASS** | J: captured payloads no-store and canary-free; forged asset negatives PASS. |
| TC-031 | Two real image pages | **PASS** | J: two actual PNG fixture pages uploaded and previewed. |
| TC-032 | Reorder/delete/re-add/cancel | **PASS** | J+R: reorder/delete/re-add browser path and cancel/unit negatives PASS. |
| TC-033 | Two-page submit and reload | **PASS** | J: order preserved, reload shows Submitted and two protected pages. |
| TC-034 | Original access matrix | **PASS** | J: student/T-A 200 exact bytes; T-X 403 on work and four originals. |
| TC-035 | Invalid media/oversize | **PASS** | R: media/size/zero-artifact negative tests PASS. |
| TC-036 | Double submit/lost ACK | **PASS** | J: double click produced one request/effect; authoritative GET reconciled. |
| TC-037 | Mid-upload failure and restart before submit | **PASS** | R: upload failure/cancel/unmount and attachment consistency tests prove no submitted/linked partial effect; successful J retry follows. |
| TC-038 | Scoped Submitted queue | **PASS** | J: T-A queue contains P01 only; T-X/foreign negatives PASS. |
| TC-039 | Desktop A4 review | **PASS** | J: `review/02-desktop-review-1440.png`, exact originals. |
| TC-040 | Tablet fullscreen/zoom/pen/undo | **PASS** | J: 768 fullscreen A4 fit and all critical controls asserted. |
| TC-041 | Two-page normalized annotations | **PASS** | J: two annotations, one stroke each, normalized coordinates/pressure survive reload/restart/restore. |
| TC-042 | NeedsRevision and student feedback | **PASS** | J: decision, feedback, overlays and student view PASS. |
| TC-043 | Resubmit and Accept | **PASS** | J: attempt 2 → Accepted, history preserved. |
| TC-044 | Accepted is terminal | **PASS** | J+R: repeat mutation controls absent/409; no extra effect. |
| TC-045 | Two reviewers/stale/lost ACK | **PASS** | J+R: lost review ACK reconciled once; stale/concurrent review tests PASS. |
| TC-046 | Student timeline without false attempts | **PASS** | J: exactly two attempts and linked reviews; no duplicate from lost ACK. |
| TC-047 | Teacher review history including Accepted | **PASS** | J: NeedsRevision and Accepted history/deep link; Accepted absent from active queue. |
| TC-048 | Exact 12×5 aggregates | **PASS** | J: 60 = Accepted 2, NeedsRevision 2, Submitted 2, NotStarted 54; P01–P04 transitions via normal login/public API, no SQL writes. |
| TC-049 | Server filtering/count protection | **PASS** | J+R: authorized filters/counts and cross-scope denials PASS. |
| TC-050 | History after move/revoke/v2 | **PASS** | R: original group/version immutable; current-policy denial after revoke PASS. |
| TC-051 | Empty group/zero denominator | **PASS** | J+R: G2/empty filters return neutral zero state, no NaN/leak. |
| TC-052 | Role/org matrix | **PASS** | J+R: wrong role/org/course/group denied with independent positive controls. |
| TC-053 | Teacher revoke and refreshed authority | **PASS** | G+R: fresh public login gets no revoked scope; protected work/files denied. |
| TC-054 | ProcessingRestrictions | **PASS** | R: deny-on-current restriction and no unsafe narrowing PASS. |
| TC-055 | Expiry/401/recovery | **PASS** | G: forced expiry 401 then refresh recovery; R error contract PASS. |
| TC-056 | Parent/file IDOR | **PASS** | J+R: missing/forged parent, foreign attachment/context denied. |
| TC-057 | Cache/logout/two orgs | **PASS** | G: memory-only tokens, no-store, two tabs, org switch, cross-tab logout PASS. |
| TC-058 | Lost ACK review reconciliation | **PASS** | J: one review request/effect; GET reconciliation; immutable history. |
| TC-059 | 409 stale/conflict | **PASS** | R: stale CAS and concurrent submissions/reviews return stable conflict. |
| TC-060 | Offline/timeout/5xx reads | **PASS** | R: retry-only-GET UI/server tests; no mutating replay. |
| TC-061 | Repeated command after client restart | **PASS** | R: command/intent receipts prevent duplicate effects. |
| TC-062 | Incomplete publication/unavailable asset | **PASS** | J+R: invalid package/asset/tamper negatives PASS. |
| TC-063 | API restart/readback | **PASS** | J `backup-restore/api-restart.json`: exact state, four SHA-256, history/progress and T-X denials. |
| TC-064 | PostgreSQL + application restart | **PASS** | J `postgres-restart.json`: new mapped connection, fresh login/readback PASS. |
| TC-065 | Coordinated backup/restore | **PASS** | J: `pg_dump -Fc` SHA/size + full private tree restored into new DB/root; `restore.json` PASS. |
| TC-066 | Incomplete restore fails explicitly | **PASS** | J: DB-only restore returns expected file read failures (500), never false PASS. |
| TC-067 | Mobile 390 | **PASS** | J: activation, lesson, hints, upload, history/feedback/Accepted screenshots and actions at 390. |
| TC-068 | Tablet 768 | **PASS** | J: review fullscreen and revision queue at 768; physical tablet not claimed. |
| TC-069 | Desktop 1440 | **PASS** | J: queue/review/progress/history navigation at 1440. |
| TC-070 | Keyboard/focus/critical controls | **PASS** | R+J: DOM role/name/focus suites and visible fullscreen controls PASS; no physical assistive-device claim. |
| TC-071 | Approved visual references | **BLOCKED (raw) / WAIVED BY OWNER FOR M1** | Исходный результат сохранён: **REFERENCE UNAVAILABLE** for every exact approved filename in checkout. Functional screenshots exist, but visual PASS is forbidden. Owner decision `M1-OD-071` снимает только обязательность для M1; gap `M1-11-R3-D02` переходит в обязательную задачу `UI-VISUAL-ACCEPTANCE` до real-student pilot. |
| TC-072 | Empty/error actions | **PASS** | J+R: empty, 401, 403, 409, upload/network unknown outcome states are distinct and non-destructive. |
| TC-073 | Full regression | **PASS** | R: final commands/results in results report; first Vitest/browser failures preserved and corrected reruns identified. |
| TC-074 | Continuous bootstrap→Accepted→progress | **PASS** | J: React E01–E11 plus same-environment E12/E13 and E14/E15 readback; no domain SQL setup or fixture bearer. |
| TC-075 | Owner decisions/boundaries | **PASS** | P: FIX-01/FIX-02 decisions applied; `PRS1 EXACT OFF · G02 BLOCKED · S4 HTTP NOT AUTHORIZED`; synthetic only. |
| TC-076 | Independent runbook reproducibility | **PASS** | I: отдельный оператор проверил immutable evidence, выполнил fresh normal-auth Chromium HTTPS E2E, isolation и restore A/B/C; подписано `TC-076 RE-REVIEW: PASS`. Historical TC-076 FAIL не изменён. |
| TC-077 | Owner acceptance decision | **PASS** | O: владелец объявил `SAPIENTUM M1 — SYNTHETIC MVP ACCEPTED` исключительно для synthetic environment. `PILOT AUTHORIZED`, production deployment и real child data не следуют. |

## UI matrix

| UI | Functional evidence | Approved reference | Verdict |
|---|---|---|---|
| U01–U03 | J screenshots; prior fixes did not regress | exact files absent | **REFERENCE UNAVAILABLE** |
| U04–U05 | course/lesson at 1440/768/390 | exact files absent | **REFERENCE UNAVAILABLE** |
| U06 | import/preview/publish screenshots | exact file absent | **REFERENCE UNAVAILABLE** |
| U07–U11 | photo/review/fullscreen/history at required widths | exact files absent | **REFERENCE UNAVAILABLE** |
| U12 | progress/history at 1440/390 | exact file absent | **REFERENCE UNAVAILABLE** |
| U13 | error/constraint states, no dedicated PNG by contract | not applicable | functional PASS only |

Исходный visual verdict U01–U12 остаётся `REFERENCE UNAVAILABLE`. Решение `WAIVED BY OWNER FOR M1` записано отдельно и не превращает ни одну строку в visual PASS. До пилота с реальными учениками обязательна задача `UI-VISUAL-ACCEPTANCE` с точными утверждёнными PNG.

## Acceptance consequence

TC-071 не является обязательным условием **только M1 Synthetic MVP**, но его исходный результат не закрыт как PASS. TC-076 re-review и TC-077 owner decision закрыты PASS. Поэтому `SYNTHETIC MVP ACCEPTED` объявлен владельцем и scoped acceptance commit разрешён. Push/deploy не выполняются; `PILOT AUTHORIZED` не объявлен.
