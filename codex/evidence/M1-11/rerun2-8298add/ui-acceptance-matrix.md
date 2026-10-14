# M1-11 UI acceptance matrix — 14.10.2026

| UI | Exact reference | Actual | Verdict |
|---|---|---|---|
| U01 activation | available | `screenshots/E02-activation-390.png` | PASS in accepted M1 scope; no public signup/email/consent invented |
| U02 first admin | available | `screenshots/E01-admin-1440.png` | PASS; loaded org/audit hierarchy |
| U03 users/groups | available | `screenshots/E02-accounts-1440.png`, `E03-roster-1440.png` | PASS; compact table and 12-student roster |
| U04 student course | REFERENCE UNAVAILABLE | — | BLOCKED after E04 |
| U05 lesson | REFERENCE UNAVAILABLE | — | BLOCKED after E04 |
| U06 materials/publication | available | E04 error state only | FAIL functional / comparison BLOCKED |
| U07 problem/photo | REFERENCE UNAVAILABLE | — | BLOCKED |
| U08 queue | REFERENCE UNAVAILABLE | — | BLOCKED |
| U09 desktop review | REFERENCE UNAVAILABLE | — | BLOCKED |
| U10 tablet fullscreen | REFERENCE UNAVAILABLE | — | BLOCKED |
| U11 history | REFERENCE UNAVAILABLE | — | BLOCKED |
| U12 teacher students | available | — | BLOCKED after E04 |
| U13 errors/restrictions | no separate PNG | actual 403 state | PASS for reached error state; full route set BLOCKED |

No unavailable reference was substituted. Static references are not browser evidence.
