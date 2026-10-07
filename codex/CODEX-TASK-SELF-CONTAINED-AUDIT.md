# CODEX-TASK-SELF-CONTAINED-AUDIT.md

## Вердикт

Текущий `CODEX-TASK-LMS-SIX-SCREENS-v2.md` **почти пригоден для запуска, но ещё не полностью самодостаточен**.

Он хорошо фиксирует:
- доменную state machine;
- 6 routes;
- основные query/command;
- photo-first сценарий;
- optimistic concurrency;
- cache invalidation;
- tests;
- правую контекстную панель;
- запрет на альтернативный workflow.

Но до передачи Codex нужно устранить несколько источников неоднозначности.

---

## P0 — исправить до запуска

### 1. Нет однозначной иерархии source of truth

В задаче перечислено несколько документов, но не сказано, что делать при расхождении.

Нужно зафиксировать порядок:

```text
1. LMS-DOMAIN-SPEC.md
2. FEATURE-SUBMISSION-REVIEW.md
3. LMS-FRONTEND-CONTRACT.md
4. LMS-UI-FLOW-SPEC.md
5. visual mockups
6. existing implementation, если он не противоречит 1–4
```

При конфликте Codex не должен самостоятельно выбирать удобную версию.

---

### 2. Пути к UI-документам устарели/неоднозначны

В task написано:

```text
LMS-UI-FLOW-SPEC-v2.md
LMS-FRONTEND-CONTRACT.md
```

и затем оговорка «если добавлены в docs/ui».

В актуальном пакете канонические пути должны быть ровно:

```text
docs/ui/LMS-UI-FLOW-SPEC.md
docs/ui/LMS-FRONTEND-CONTRACT.md
```

Версионность лучше хранить внутри документа, а не в filename.

---

### 3. Визуальный референс отсутствует в репозитории

Task требует:

> использовать visual language ленты событий Sapientum

и описывает стиль словами, но Codex не видит изображения из чата.

Если требуется соответствие именно последним макетам, в repository необходимо положить хотя бы один файл:

```text
docs/ui/mockups/lms-six-screens-right-panel.png
```

и явно объявить его visual reference.

Без этого Codex может реализовать функционально правильный, но визуально другой интерфейс.

---

### 4. Правила правой панели добавлены как приложение и частично расходятся с ранним текстом

Например, раннее описание Screen 6 задаёт правую панель как:

```text
Feedback editor
Вернуть на доработку
Принять решение
```

а дополнение требует ещё:

```text
предыдущие попытки
контекст
быстрые заметки
actions
```

Codex должен получить один итоговый контракт, а не базовый текст + patch в конце.

Нужно считать секцию про обязательную правую панель последней и нормативной либо интегрировать её в описание каждого screen.

---

### 5. Не зафиксирован конкретный repository layout

В task нужно явно указать ожидаемую структуру проекта, а при несовпадении — discovery, а не создание новой архитектуры:

```text
src/Lms.Api
src/Lms.Application
src/Lms.Domain
src/Lms.Infrastructure
src/Lms.Web
```

Codex должен сначала подтвердить реальные пути.

---

## P1 — желательно исправить

### 6. Не зафиксировано, какие read endpoints уже существуют, а какие являются обязательным gap

Нужно явно разделить:

**commands / feature endpoints — проверить и переиспользовать:**
```text
POST /api/v1/uploads
POST /api/v1/problem-assignments/{id}/submissions
POST /api/v1/problem-works/{id}/messages
POST /api/v1/problem-works/{id}/reviews/needs-revision
POST /api/v1/problem-works/{id}/reviews/accept
GET  /api/v1/problem-works/{id}
GET  /api/v1/teacher/review-queue
```

**новые read-model endpoints, если отсутствуют:**
```text
GET /api/v1/courses/{courseId}/workspace
GET /api/v1/courses/{courseId}/lessons/{lessonId}/workspace
GET /api/v1/problem-assignments/{assignmentId}/workspace
```

---

### 7. Не задана политика изменений schema/migrations

Для экранов большая часть новых данных — projections.

Нужно запретить migration «на всякий случай»:

```text
Не менять aggregate/schema ради удобства UI.
Migration допустима только если существующее хранилище объективно не поддерживает уже утверждённый feature contract.
```

---

### 8. Не хватает обязательной проверки существующих reusable components

Она упомянута, но полезно потребовать таблицу перед реализацией:

```text
Need | Existing component | Reuse/extend/create | Evidence
```

Это резко снижает риск параллельной UI-библиотеки.

---

### 9. Нет явного правила для scope creep

Codex должен остановиться и перечислить blocker, а не реализовывать:
- календарь;
- полноценный chat;
- rubric/scoring;
- notification system;
- серверный draft subsystem;
- новый design system.

---

### 10. Не зафиксирована стратегия draft persistence

Frontend contract допускает browser-local draft.

Task должен сказать:

```text
MVP: использовать существующий draft mechanism, если он есть.
Если его нет — local/browser draft допустим.
Не создавать серверный Draft aggregate/use case без отдельного решения.
```

---

## P2 — улучшает качество выполнения

### 11. Нужен preflight report

До кода Codex должен выдать/записать:

```text
- найденные project paths;
- найденные routes/endpoints/use cases;
- найденные reusable components;
- gaps;
- migrations required? yes/no;
- tests/build commands;
- implementation plan.
```

---

### 12. Нужны checkpoints

Лучше реализовать по checkpoint:

```text
A. Read models/API
B. AppShell + right panel
C. Student slice
D. Teacher slice
E. E2E + DDD review
```

После каждого checkpoint запускать targeted tests.

---

## Итог

После исправления P0 task можно считать самодостаточным для работы Codex в repository.

Главный отсутствующий артефакт сейчас — **визуальный mockup внутри repository**. Все domain/UI contracts уже достаточно подробны.
