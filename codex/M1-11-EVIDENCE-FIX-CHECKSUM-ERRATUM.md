# ERRATUM — M1-11 TC-076 checksum baseline

Дата исправления: 2026-10-14  
Время фиксации: `2026-10-14T01:12:07+00:00`  
Автор исправления: **OpenAI Codex, оператор M1-11-EVIDENCE-FIX**  
Область: только документальный checksum baseline; исходный `rerun3-688d8cf` не изменён.

## Исправляемая запись

Файл с ошибкой: `codex/M1-11-TC-076-INDEPENDENT-REVIEW-PACKAGE.md`, исходная строка таблицы `final-journey-2/`.

- Исходное ошибочное значение (65 hex-символов): `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebbc`.
- Корректное значение SHA-256 (64 hex-символа): `8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebb`.

Команда расчёта из `/home/tea/github/lms`:

```bash
find codex/evidence/M1-11/rerun3-688d8cf/final-journey-2 -type f -print0 | sort -z | xargs -0 sha256sum | sha256sum
```

Фактический вывод:

```text
8b20a2cbf5ec939efe8365ed953544bf179c11aa3c042ce03db8206c03d9eebb  -
```

## Причина

Raw checksum audit уже содержал корректное 64-символьное значение. При переносе этого значения в строку review package после полного digest был добавлен один лишний завершающий символ `c`. Поиск по checkout не выявил другого источника ошибочного значения: оно присутствовало только в рабочем package и в историческом независимом FAIL-отчёте, который цитирует найденное расхождение. Это редакционная ошибка baseline, а не изменение evidence и не попытка подогнать исходный файл.

## Контроль неизменности

| Объект | SHA-256 |
|---|---|
| Полный исходный `rerun3-688d8cf`, 681 файл, aggregate по документированному алгоритму — до исправления | `186d4e4a9da3b52210bb9f4c3241c9ed025d9b9917e56eef7c083d9976ef5838` |
| Полный исходный `rerun3-688d8cf`, повторная проверка перед erratum | `186d4e4a9da3b52210bb9f4c3241c9ed025d9b9917e56eef7c083d9976ef5838` |
| `independent-20261014-codex/checksum-audit.txt` | `b20fecdb8aca768f30fbd89b7dbabc11803fd6642bf4fb34a1ed75875386ac40` |
| Первоначальный ошибочный review package, сохранённая byte-for-byte копия | `9f60c2d8db303a8253eb0cea529e844515597c86391ddadf835fc5708e1f2595` |
| Исторический `TC-076-INDEPENDENT-REVIEW-RESULTS.md` (`TC-076: FAIL`, не изменён) | `a3c81a8c8b08954b4fabf2a1b59f691fb1eb85da2eedf665d75251bae7c2c447` |

Историческая ошибочная версия package сохранена в `codex/evidence/M1-11/evidence-fix-20261014-codex/historical/M1-11-TC-076-INDEPENDENT-REVIEW-PACKAGE-original-erroneous.md`.

Erratum не меняет исходные `74 PASS / 0 FAIL / 3 BLOCKED`, не переписывает `TC-076: FAIL` в `PASS` и не является новой независимой аттестацией.
