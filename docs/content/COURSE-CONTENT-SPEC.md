# Course Content Specification

**Status:** Draft  
**Version:** 1.0  
**Schema family:** `course/v1`, `lesson/v1`, `problem/v1`  
**Primary representation:** Markdown + YAML Front Matter + local static assets

---

## 1. Назначение

Настоящая спецификация определяет нормативный формат хранения учебного контента олимпиадного курса.

Формат предназначен для:

- хранения курса в обычных текстовых файлах;
- редактирования курса без CMS и специализированного редактора;
- версионирования курса в Git;
- автоматической генерации и изменения материалов средствами Codex и других инструментов;
- валидации курса до публикации;
- публикации неизменяемой версии курса в LMS;
- одинакового отображения Markdown, математических формул, ссылок, изображений, файлов и видео.

Спецификация описывает **контент курса**, но не состояние учебного процесса.

К состоянию LMS, не входящему в настоящий контракт, относятся:

- пользователи;
- преподаватели;
- группы;
- назначения;
- сроки;
- прогресс;
- посещаемость;
- попытки решения;
- диалоги;
- проверки;
- оценки;
- статусы выполнения;
- история взаимодействия ученика и преподавателя.

---

## 2. Нормативные термины

Слова **MUST**, **MUST NOT**, **REQUIRED**, **SHOULD**, **SHOULD NOT**, **MAY** имеют нормативный смысл.

- **MUST / REQUIRED** — требование обязательно.
- **MUST NOT** — действие запрещено.
- **SHOULD** — требование настоятельно рекомендуется; отступление допустимо только при наличии причины.
- **SHOULD NOT** — действие настоятельно не рекомендуется.
- **MAY** — действие допускается, но не является обязательным.

---

## 3. Архитектурные принципы

### 3.1. Разделение контента и состояния

Учебный контент MUST храниться отдельно от состояния LMS.

Контент курса MUST быть представлен Markdown-файлами и статическими ресурсами.

LMS MUST NOT использовать Markdown-файлы курса как хранилище:

- прогресса ученика;
- комментариев;
- статусов проверки;
- сообщений;
- попыток решения;
- оценок;
- сроков;
- индивидуальных назначений.

### 3.2. Markdown как основной формат

Markdown MUST быть основным представлением текстового контента курса.

Курс MUST оставаться читаемым без LMS.

Спецификация SHOULD избегать проприетарного DSL, если ту же задачу можно решить стандартным Markdown.

### 3.3. Отсутствие CMS

LMS MUST NOT требовать наличия визуального редактора материалов курса.

Курс MAY редактироваться в любом текстовом редакторе, через Git, Codex и другие автоматизированные инструменты.

### 3.4. Версионная публикация

LMS MUST отображать опубликованную версию курса.

Изменение файлов в исходном репозитории MUST NOT автоматически изменять уже опубликованный курс.

---

## 4. Структура курса

Корневой каталог курса MUST содержать файл `course.md`.

Рекомендуемая структура:

```text
course/
├── course.md
├── assets/
│   ├── images/
│   └── files/
└── lessons/
    ├── 01-introduction/
    │   ├── lesson.md
    │   ├── assets/
    │   │   ├── images/
    │   │   └── files/
    │   └── problems/
    │       ├── 01-first-problem.md
    │       └── 02-second-problem.md
    └── 02-divisibility/
        ├── lesson.md
        └── problems/
            ├── 01.md
            └── 02.md
```

Курс MUST содержать `course.md` и каталог `lessons/`.

Каждый урок MUST содержать `lesson.md`.

Каждая задача MUST храниться в отдельном `.md`-файле внутри `problems/`.

Курс и урок MAY содержать каталоги `assets/images/` и `assets/files/`.

---

## 5. Идентификаторы

Каждый `course`, `lesson` и `problem` MUST иметь стабильный `id`.

`id` MUST соответствовать:

```regex
^[a-z][a-z0-9-]*$
```

Валидные значения:

```text
euler-7
divisor-set
reliable-number
problem-01
```

Невалидные:

```text
Euler-7
01-problem
problem_01
problem 01
задача-1
```

Имя файла и путь MUST NOT считаться бизнес-идентификатором.

При переименовании файла или каталога `id` SHOULD оставаться неизменным.

`course.id` MUST быть глобально уникальным в пределах системы публикации.

`lesson.id` MUST быть уникальным внутри курса.

`problem.id` SHOULD быть уникальным внутри курса.

---

## 6. `course.md`

Файл MUST начинаться с YAML Front Matter.

Минимальный валидный пример:

```markdown
---
schema: course/v1
id: euler-7
title: Подготовка к олимпиаде Эйлера
---

# Подготовка к олимпиаде Эйлера

Курс предназначен для подготовки к олимпиадным задачам.
```

Обязательные поля:

```yaml
schema: course/v1
id: <course-id>
title: <course-title>
```

Необязательное поле:

```yaml
description: <short-description>
```

Неизвестное поле SHOULD приводить к `WARNING`.

Неизвестное поле MUST NOT молча менять поведение LMS.

---

## 7. `lesson.md`

Пример:

```markdown
---
schema: lesson/v1
id: divisor-set
title: Множество делителей
order: 9
---

# Множество делителей

Сегодня мы изучим делители натурального числа.

## Видео

https://rutube.ru/video/example/

## Определение

Число $d$ называется делителем числа $n$, если существует
натуральное число $k$, для которого

$$
n = dk.
$$
```

Обязательные поля:

```yaml
schema: lesson/v1
id: <lesson-id>
title: <lesson-title>
order: <integer>
```

`order` MUST быть целым положительным числом и MUST быть уникальным внутри курса.

Порядок уроков MUST определяться полем `order`.

Имя каталога MAY содержать числовой префикс, но MUST NOT определять семантику порядка.

---

## 8. `problem.md`

Каждая задача MUST находиться в отдельном Markdown-файле.

Пример:

```markdown
---
schema: problem/v1
id: reliable-number
title: Надёжные числа
order: 1
source: Олимпиада Эйлера, 2025, 7 класс, региональный этап, №4
---

Натуральное число $n$ назовём **надёжным**, если сумма
нескольких его различных делителей, среди которых есть $1$,
равна $n$.

Любое ли натуральное число является делителем некоторого
надёжного числа?
```

Обязательные поля:

```yaml
schema: problem/v1
id: <problem-id>
order: <integer>
```

Необязательные поля:

```yaml
title: <problem-title>
source: <source-description>
```

`order` MUST быть уникальным внутри урока.

---

## 9. Граница видимости задачи

`problem.md` MUST содержать только контент, разрешённый ученику.

Он MUST NOT содержать скрытые:

- решения;
- подсказки;
- ответы;
- комментарии преподавателя;
- rubric;
- диагностические данные.

Если решения и подсказки понадобятся, они SHOULD храниться отдельно, например:

```text
01.md
01.solution.md
01.hints.md
```

Эти файлы не входят в обязательный v1.

---

## 10. Поддерживаемый Markdown

Реализация MUST поддерживать:

- заголовки;
- абзацы;
- жирный и курсивный текст;
- маркированные и нумерованные списки;
- цитаты;
- inline code;
- code blocks;
- ссылки;
- изображения;
- таблицы.

---

## 11. Raw HTML

Raw HTML MUST NOT поддерживаться.

Запрещены в том числе:

```html
<script>
<iframe>
<style>
<object>
<embed>
<form>
<input>
<button>
```

HTML event handlers MUST NOT допускаться.

Видео MUST встраиваться через разрешённые URL, а не через `<iframe>` автора курса.

---

## 12. Математические формулы

Реализация MUST поддерживать inline-формулы:

```markdown
Если $a \\mid b$, то $a$ является делителем $b$.
```

и display-формулы:

```markdown
$$
n=p_1^{\\alpha_1}p_2^{\\alpha_2}\\cdots p_k^{\\alpha_k}.
$$
```

Рендерер SHOULD использовать KaTeX или эквивалентный безопасный механизм.

LaTeX MUST использоваться только для математической нотации.

---

## 13. Изображения

Локальные изображения MUST использовать относительные пути:

```markdown
![Разбиение на пары](./assets/images/divisor-pairs.svg)
```

Допустимые форматы SHOULD включать:

```text
.png
.jpg
.jpeg
.webp
.svg
```

Внешние изображения в `course/v1` MUST NOT использоваться.

SVG MAY использоваться только с безопасной обработкой/санитизацией.

---

## 14. Файлы

Локальные файлы MAY подключаться обычной Markdown-ссылкой:

```markdown
[Листок для печати](./assets/files/worksheet.pdf)
```

LMS MAY отображать ссылку как визуальную карточку файла.

Presentation logic MUST NOT менять семантику исходного Markdown.

---

## 15. Внешние ссылки

Внешние ссылки MAY использовать `https://`.

Следующие schemes MUST NOT использоваться:

```text
javascript:
data:
file:
ftp:
```

`http://` SHOULD считаться ошибкой либо предупреждением.

Внешние ссылки SHOULD открываться в новой вкладке.

При `target="_blank"` MUST использоваться `rel="noopener noreferrer"`.

---

## 16. Видео

Видео MUST задаваться обычной URL на отдельной строке:

```markdown
https://rutube.ru/video/xxxxxxxx/
```

LMS MAY преобразовывать URL поддерживаемого провайдера в embedded player.

v1 SHOULD использовать whitelist провайдеров, например:

```text
rutube.ru
vkvideo.ru
youtube.com
youtu.be
```

Неподдерживаемый URL MUST отображаться как обычная ссылка.

Автор курса MUST NOT вставлять `<iframe>`.

---

## 17. Пользовательские вложения

Контент курса и пользовательские вложения MUST храниться отдельно.

Контент курса:

```text
Git / filesystem / published content storage
```

Пользовательские вложения:

```text
object storage
```

Сообщения MAY ссылаться на вложения через внутренний URI:

```text
attachment://<attachment-id>
```

`attachment://` MUST NOT использоваться в файлах курса.

---

## 18. Markdown сообщений

Сообщения ученика и преподавателя SHOULD использовать то же безопасное подмножество Markdown.

Они MAY содержать:

- текст;
- формулы;
- ссылки;
- цитаты;
- изображения-вложения;
- файлы-вложения.

---

## 19. Цитирование сообщений

Markdown quote MAY использоваться для цитирования:

```markdown
> Поэтому число делителей равно $(3+1)(2+1)=12$.

Верно.
```

LMS SHOULD отдельно хранить `ReplyToMessageId`.

Markdown-цитата MUST NOT заменять семантическую связь ответа.

---

## 20. Внутренние ссылки

Относительные ссылки на локальные ресурсы MAY использоваться.

Ссылки между уроками через файловые пути SHOULD NOT использоваться.

В будущей версии MAY появиться логическая схема:

```text
lms://lesson/primes
lms://problem/reliable-number
```

Она не является частью обязательного v1.

---

## 21. Front Matter

Front Matter MUST быть YAML и MUST быть ограничен разделителями `---`.

Front Matter MUST описывать семантику.

Он MUST NOT использоваться для произвольного управления UI.

Запрещённые примеры:

```yaml
fontSize: 24
background: red
hideHeader: true
layout: compact
cssClass: custom
```

---

## 22. Версионирование схемы

Каждый документ MUST содержать `schema`.

Допустимые значения v1:

```text
course/v1
lesson/v1
problem/v1
```

Неподдерживаемая schema MUST приводить к ошибке валидации.

Система MUST NOT угадывать schema по имени файла.

---

## 23. Валидация

Перед публикацией курс MUST пройти автоматическую валидацию.

Валидатор MUST проверять:

- наличие `course.md`;
- наличие `lessons/`;
- наличие `lesson.md`;
- корректность YAML Front Matter;
- обязательные поля;
- поддерживаемую schema;
- формат и уникальность `id`;
- уникальность `order`;
- существование локальных ссылок и изображений;
- запрещённые URI schemes;
- raw HTML;
- запрещённые embed-конструкции;
- корректность путей;
- выход за корень курса;
- допустимые размеры ресурсов;
- видео-provider;
- парсинг Markdown;
- математические формулы.

---

## 24. Ошибки и предупреждения

Валидатор MUST различать `ERROR` и `WARNING`.

`ERROR` MUST блокировать публикацию.

`WARNING` SHOULD позволять публикацию.

Примеры:

```text
ERROR: Missing required field "id"
ERROR: Duplicate lesson id "divisibility"
ERROR: Unsafe URL scheme "javascript:"
ERROR: Raw HTML is not allowed
ERROR: Path escapes course root

WARNING: Unknown front matter field "authorComment"
WARNING: Filename order differs from front matter order
WARNING: Unsupported video provider; link will be rendered as a normal link
```

---

## 25. Безопасность путей

Все локальные пути MUST нормализоваться.

Путь MUST оставаться внутри корня курса.

Невалидный пример:

```markdown
[secret](../../../../etc/passwd)
```

Symlink, ведущий за пределы курса, MUST считаться невалидным.

---

## 26. Недоверенный ввод

Markdown, Front Matter и ресурсы MUST считаться недоверенным вводом.

Даже контент из Git MUST проходить проверку.

Значения вроде:

```yaml
title: "<script>alert(1)</script>"
```

MUST отображаться безопасно как текст и MUST NOT исполняться.

---

## 27. Санитизация

Markdown renderer MUST санитизировать результирующий HTML.

Запрет raw HTML MUST NOT считаться достаточной защитой.

Рендеринг SHOULD использовать whitelist разрешённых тегов и атрибутов.

---

## 28. Ограничения размеров

Реализация MUST иметь конфигурируемые лимиты.

Рекомендуемые значения по умолчанию:

```text
Markdown document ≤ 1 MB
Image             ≤ 10 MB
Static file       ≤ 50 MB
Course package    ≤ configurable limit
```

Превышение лимита MUST блокировать публикацию.

---

## 29. Публикация курса

Исходный курс SHOULD храниться в Git.

Рекомендуемый pipeline:

```text
Git
 ↓
Course Import
 ↓
Validation
 ↓
Build
 ↓
Immutable Published Version
 ↓
LMS
```

LMS MUST NOT обслуживать пользовательские запросы непосредственно из рабочей копии Git.

---

## 30. Неизменяемая опубликованная версия

Опубликованная версия MUST быть неизменяемой.

Каждая версия SHOULD иметь:

```text
CourseId
VersionId
SourceCommit
PublishedAt
```

Изменение исходников MUST создавать новую опубликованную версию.

---

## 31. Привязка задания к версии

Назначение ученику MUST быть исторически воспроизводимым.

Если ученик сдаёт решение, LMS MUST иметь возможность определить, какое условие он видел.

Поэтому назначение SHOULD ссылаться на опубликованную версию:

```text
ProblemAssignment
- ProblemId
- ContentVersionId
```

Изменение `problem.md` MUST NOT ретроактивно менять условие старой попытки.

---

## 32. Имена файлов

Имена файлов MAY содержать числовые префиксы:

```text
01.md
02-divisibility.md
03-reliable-number.md
```

Префикс MUST NOT определять порядок.

Порядок MUST определяться `order`.

---

## 33. Не входит в v1

В базовый v1 MUST NOT входить:

- произвольный HTML;
- JavaScript;
- CSS;
- arbitrary iframe/embed;
- conditional blocks;
- переменные;
- шаблоны;
- include Markdown-файлов;
- собственный DSL;
- интерактивные виджеты;
- встроенные скрытые решения;
- ветвящийся контент;
- программируемая логика.

---

## 34. Валидные примеры

### Валидный `course.md`

```markdown
---
schema: course/v1
id: euler-7
title: Подготовка к олимпиаде Эйлера
description: Продвинутый курс
---

# Подготовка к олимпиаде Эйлера

Курс по олимпиадной математике.
```

### Валидный `lesson.md`

```markdown
---
schema: lesson/v1
id: divisibility
title: Делимость
order: 2
---

# Делимость

Если $a \\mid b$, то существует натуральное $k$ такое, что

$$
b=ak.
$$

https://rutube.ru/video/example/

[Листок](./assets/files/worksheet.pdf)
```

### Валидный `problem.md`

```markdown
---
schema: problem/v1
id: reliable-number
title: Надёжные числа
order: 1
source: Олимпиада Эйлера
---

Натуральное число $n$ назовём **надёжным**, если сумма
нескольких его различных делителей, среди которых есть $1$,
равна $n$.

Любое ли натуральное число является делителем некоторого
надёжного числа?
```

---

## 35. Невалидные примеры

### Нет schema

```markdown
---
id: divisibility
title: Делимость
order: 2
---
```

```text
ERROR: Missing required field "schema"
```

### Невалидный id

```yaml
id: Делимость-01
```

```text
ERROR: Invalid id format
```

### Raw HTML

```markdown
<iframe src="https://example.org"></iframe>
```

```text
ERROR: Raw HTML is not allowed
```

### JavaScript URL

```markdown
[Открыть](javascript:alert(1))
```

```text
ERROR: Unsafe URL scheme "javascript:"
```

### Выход из каталога

```markdown
[Файл](../../../secret.txt)
```

```text
ERROR: Path escapes course root
```

### Внешнее изображение

```markdown
![image](https://example.org/image.png)
```

```text
ERROR: External images are not allowed in course/v1
```

---

## 36. CLI валидатора

CLI SHOULD поддерживать:

```text
course validate <path>
```

Пример:

```text
course validate ./courses/euler-7
```

Результат:

```text
Course: euler-7
Lessons: 18
Problems: 126

Warnings: 2
Errors: 0

VALID
```

Коды возврата SHOULD быть:

```text
0 — valid
1 — validation errors
2 — internal validator error
```

---

## 37. Требования к Codex

При изменении курса Codex MUST:

1. сохранять schema;
2. сохранять существующие стабильные `id`, если сущность остаётся той же;
3. не использовать raw HTML;
4. не добавлять внешние изображения;
5. не создавать небезопасные URI;
6. не смешивать контент и состояние LMS;
7. не помещать скрытые решения в `problem.md`;
8. после изменений запускать Course Validator;
9. не считать работу завершённой при наличии `ERROR`.

Codex SHOULD минимизировать изменения, не относящиеся к поставленной задаче.

---

## 38. Совместимость

Парсер `course/v1` SHOULD выдавать предупреждение для неизвестных полей.

Парсер MUST NOT придавать неизвестному полю новую семантику.

Изменение семантики существующего поля MUST требовать новой версии schema.

---

## 39. Краткая модель

```text
Course
│
├── course.md
│
└── Lesson*
    │
    ├── lesson.md
    ├── assets/*
    └── Problem*
        └── problem.md
```

---

## 40. Основной архитектурный инвариант

> Markdown определяет, **что показать**.  
> LMS Domain определяет, **что происходит**.

Контент курса MUST оставаться независимым от UI-фреймворка, ORM, базы данных, API и конкретной реализации frontend/backend.
