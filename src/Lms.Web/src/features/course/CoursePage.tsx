import { ContentPage } from '../content/ContentPage';
import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import type { ApiClient, ApiError } from '../../api/client';
import type { CourseWorkspace, CourseLessonRow } from '../../api/contracts/courseWorkspace';
import { AppShell, ContextSection } from '../../layout/AppShell';
import { AvailabilityBadge } from '../../components/StatusBadge';
import { Breadcrumbs, EmptyState, ErrorState, LoadingSkeleton, PageHeader, ProgressBar } from '../../components/primitives';
import { useCourseWorkspace } from './query';
import './course.css';

const lessonPresentation = {
  NotStarted: { label: 'Не начат', tone: 'neutral', action: 'Открыть' },
  InProgress: { label: 'В работе', tone: 'info', action: 'Продолжить' },
  Completed: { label: 'Завершён', tone: 'success', action: 'Посмотреть' },
} as const;
function availabilityExplanation({ reason, availableFrom }: CourseLessonRow['availability']) {
  if (reason === 'Scheduled') return availableFrom
    ? `Откроется ${new Intl.DateTimeFormat('ru', { dateStyle: 'long', timeStyle: 'short' }).format(new Date(availableFrom))}`
    : 'Открытие запланировано';
  if (reason === 'NotPublished') return 'Урок ещё не опубликован';
  if (reason === 'EnrollmentInactive') return 'Обучение на курсе неактивно';
  return reason && reason !== 'Available' ? `Недоступен: ${reason}` : 'Недоступен';
}
/** Course-specific links accept web/relative URLs only, never executable schemes. */
function safeMaterialUrl(value: string): string | null {
  try {
    const url = new URL(value, window.location.origin);
    return ['https:', 'http:'].includes(url.protocol) ? url.href : null;
  } catch { return null; }
}

function CourseContext({ data }: { data: CourseWorkspace }) {
  const { completedLessons, availableLessons, percent } = data.progress;
  const teacher = data.course.teacher;
  return <>
    <ContextSection title="Прогресс курса">
      <p>{completedLessons} из {availableLessons} доступных уроков завершено</p>
      <ProgressBar value={percent} label="Завершение курса" />
      {data.teacherContext && <p className="muted course-progress-note">По доступным вам работам курса и групп.</p>}
    </ContextSection>
    {teacher && <ContextSection title="Преподаватель">
      <p>{teacher.displayName?.trim() || 'Имя преподавателя не указано'}</p>
    </ContextSection>}
    {data.materials.length > 0 && <ContextSection title="Материалы курса"><ul className="course-materials">
      {data.materials.map(material => {
        const href = safeMaterialUrl(material.url);
        return <li key={material.id}>{href ? <a href={href}>{material.title}</a> : <span>{material.title}</span>}
          {material.description && <p className="muted">{material.description}</p>}
          {!href && <p className="muted">Ссылка недоступна</p>}
        </li>;
      })}
    </ul></ContextSection>}
  </>;
}

function Lessons({ data }: { data: CourseWorkspace }) {
  return <section aria-labelledby="course-lessons-heading" className="course-lessons">
    <div className="section-intro course-lessons-heading"><h2 id="course-lessons-heading">Уроки</h2>
      <span className="muted">Всего: {data.progress.totalLessons}</span></div>
    {data.lessons.length === 0 ? <EmptyState title="В курсе пока нет уроков" /> : <>
      {data.progress.availableLessons === 0 && <p className="course-notice">Пока нет доступных уроков.</p>}
      <ol className="course-lesson-list">{data.lessons.map(row => {
        const { lesson, availability, progress } = row;
        const presentation = lessonPresentation[progress.status];
        const title = lesson.title?.trim() || `Урок ${lesson.order}`;
        return <li key={lesson.id} className={`course-lesson ${availability.isAvailable ? '' : 'course-lesson--locked'}`}>
          <span className="row-number" aria-label={`Урок ${lesson.order}`}>{lesson.order}</span>
          <div className="course-lesson-content"><h3>{title}</h3>
            {availability.isAvailable && lesson.summary && <p className="muted">{lesson.summary}</p>}
            <div className="course-lesson-meta">
              <AvailabilityBadge isAvailable={availability.isAvailable} explanation={availabilityExplanation(availability)} />
              <span className={`badge badge--${presentation.tone}`}>{presentation.label}</span>
              {availability.isAvailable && <span className="muted small">Принято задач: {progress.acceptedProblems} из {progress.totalAssignedProblems}</span>}
            </div>
          </div>
          {availability.isAvailable ? <Link className="course-lesson-link" aria-label={`${presentation.action}: ${title}`}
            to={`/courses/${encodeURIComponent(data.course.id)}/lessons/${encodeURIComponent(lesson.id)}`}>
            {presentation.action}<span aria-hidden="true"> →</span>
          </Link> : <span className="course-locked-label"><span aria-hidden="true">⊘ </span>Недоступен</span>}
        </li>;
      })}</ol>
    </>}
  </section>;
}

function CourseFailure({ error, retry }: { error: ApiError; retry: () => void }) {
  if (error.status === 401) return <ErrorState title="Войдите, чтобы открыть курс"
    description="Требуется вход в учебную систему. Если сессия завершилась, войдите повторно." />;
  if (error.status === 403) return <ErrorState title="Нет доступа к курсу"
    description="У вашей учётной записи нет доступа к этому курсу." />;
  if (error.status === 404) return <ErrorState title="Курс не найден"
    description="Проверьте ссылку: курс не существует или недоступен." />;
  return <ErrorState title="Не удалось загрузить курс" description={error.code === 'ContentUnavailable'
    ? 'Материалы курса временно недоступны. Попробуйте ещё раз позже.'
    : error.code === 'InvalidResponse' ? 'Получен некорректный ответ сервера.'
    : 'Не удалось получить данные. Проверьте соединение и повторите попытку.'}
    onRetry={retry} />;
}

export function CoursePage({ courseId, client }: { courseId: string; client: ApiClient }) {
  const { state, retry } = useCourseWorkspace(client, courseId);
  const data = state.phase === 'success' ? state.data : null;
  const title = data ? data.course.title?.trim() || 'Курс без названия' : state.phase === 'loading' ? 'Загрузка курса' : 'Курс';
  useEffect(() => { document.title = `${title} · Sapientum`; }, [title]);
  return <AppShell role={data?.teacherContext ? 'Teacher' : 'Student'} preview={false} context={
    state.phase === 'loading' ? <ContextSection title="Прогресс курса"><LoadingSkeleton label="Загрузка контекста курса" rows={3} /></ContextSection>
      : data ? <CourseContext data={data} /> : null
  }>
    <Breadcrumbs items={[{ label: 'Мои курсы', to: '/courses' }, { label: data ? title : 'Курс' }]} />
    <div className="workspace-content course-workspace" aria-busy={state.phase === 'loading'}>
      <PageHeader title={title} description={data?.course.description ?? undefined} meta={data && <>
        {data.course.gradeLabel && <span>{data.course.gradeLabel}</span>}
        {data.teacherContext && <span>Режим преподавателя{data.teacherContext.canReviewCourse ? ' · Доступ к проверке работ' : ''}</span>}
      </>} />
      {state.phase === 'loading' && <div className="course-loading"><LoadingSkeleton label="Загрузка описания курса" rows={2} />
        <LoadingSkeleton label="Загрузка уроков" rows={6} /></div>}
      {state.phase === 'error' && <CourseFailure error={state.error} retry={retry} />}
      {data && <Lessons data={data} />}
    </div>
  </AppShell>;
}

export function CourseContentPage({ path, client }: { path: string; client: ApiClient }) {
  return <ContentPage kind="course" path={path} client={client} />;
}
