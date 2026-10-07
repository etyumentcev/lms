# LMS Domain Boundary

## Основная граница

**Markdown отвечает на вопрос: «что показать?»**

**LMS Domain отвечает на вопрос: «что происходит?»**

## В Markdown

Хранятся:

- описание курса;
- текст урока;
- теория;
- формулы;
- ссылки;
- видео-ссылки;
- изображения;
- файлы;
- условия задач;
- источник задачи.

## В LMS / PostgreSQL

Хранятся:

- пользователи;
- роли;
- группы;
- зачисление на курс;
- назначения;
- сроки;
- доступность урока;
- прогресс;
- попытки решения;
- статусы;
- сообщения;
- проверки;
- оценки;
- история событий.

## Базовые сущности LMS

```text
Student
Teacher

Course
CourseEnrollment

Group
GroupMembership

LessonReference
ProblemReference

ProblemAssignment

Submission
SubmissionAttachment

Thread
Message
Review

LessonProgress
ProblemProgress
```

`LessonReference` и `ProblemReference` не должны дублировать тело Markdown.
Они связывают LMS Domain с опубликованной версией контента.

## Сообщения

Сообщение может иметь модель:

```text
Message
- Id
- ThreadId
- AuthorId
- BodyMarkdown
- ReplyToMessageId?
- CreatedAt
- EditedAt?
```

`BodyMarkdown` поддерживает формулы, ссылки и цитаты.

`ReplyToMessageId` хранит семантическую связь ответа и не заменяется Markdown-цитатой.

## Вложения

Пользовательские вложения хранятся в object storage.

```text
Attachment
- Id
- OwnerId
- FileName
- ContentType
- Size
- StorageKey
- CreatedAt
```

В `BodyMarkdown` MAY использоваться:

```text
attachment://<attachment-id>
```

## Попытки решения

Одна задача MAY иметь несколько попыток:

```text
ProblemAssignment
       ↓
Submission #1
       ↓
Review: NeedsRevision
       ↓
Submission #2
       ↓
Review: Accepted
```

Это принципиально для олимпиадной подготовки: система должна хранить процесс поиска и доработки решения, а не только финальный ответ.
