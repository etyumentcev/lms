import { useEffect, useRef, useState } from 'react';
import { Link, Route, Routes, useLocation, matchPath } from 'react-router-dom';
import { AppShell, ContextSection } from './layout/AppShell';
import type { NavigationRole } from './layout/SidebarNavigation';
import { AsyncButton, Breadcrumbs, EmptyState, ErrorState, InlineError, LoadingSkeleton, PageHeader, ProgressBar, Section } from './components/primitives';
import { AvailabilityBadge, StatusBadge, statusPresentation } from './components/StatusBadge';
import type { ProblemWorkStatus } from './components/StatusBadge';
import { Tabs } from './components/Tabs';
import { ProblemPage } from './features/problem-workspace/ProblemPage';
import { LessonPage } from './features/lesson/LessonPage';
import { normalizeQueueQuery } from './features/review-queue/query';
import { ReviewQueuePage } from './features/review-queue/ReviewQueuePage';
import { CoursePage } from './features/course/CoursePage';
import { browserApiClient } from './api/browserClient';
import { AttachmentUploader } from './components/attachments/AttachmentUploader';
import { attachmentApi } from './components/attachments/api';
import type { ApiClient } from './api/client';

const demos = [
  { title: 'Курс', subtitle: 'Уроки и материалы', to: '/courses/demo-course' },
  { title: 'Урок', subtitle: 'Задачи и контекст урока', to: '/courses/demo-course/lessons/demo-lesson' },
  { title: 'Задача · Решение', subtitle: 'Рабочее пространство ученика', to: '/problem-assignments/demo-assignment' },
  { title: 'Задача · История', subtitle: 'Вкладка в пространстве задачи', to: '/problem-assignments/demo-assignment?tab=history' },
  { title: 'Проверка', subtitle: 'Рабочее пространство преподавателя', to: '/teacher/reviews' },
  { title: 'Проверка решения', subtitle: 'Контекст отдельной работы', to: '/teacher/reviews/demo-work' },
] as const;

function ShellDemo() {
  const [tab, setTab] = useState('spaces');
  return <>
    <PageHeader title="Учебное пространство" meta={<span>Демонстрация интерфейса</span>}
      description="Курсы, задачи и проверка решений — в одном пространстве." />
    <Tabs label="Демонстрация оболочки" value={tab} onChange={setTab} items={[
      { id: 'spaces', label: 'Разделы', content: <>
        <div className="section-intro"><h2>Рабочие пространства</h2><span className="muted">6 экранов</span></div>
        <p className="muted">Здесь можно посмотреть навигацию. Содержимое разделов появится позже.</p>
        <ol className="workspace-list">{demos.map((demo, index) => <li key={demo.to}>
          <Link to={demo.to}><span className="row-number">{index + 1}</span>{' '}
            <span className="row-text"><strong>{demo.title}</strong>{' '}<span className="muted">{demo.subtitle}</span></span>
            <span className="row-arrow" aria-hidden="true">→</span></Link>
        </li>)}</ol>
      </> },
      { id: 'components', label: 'Элементы интерфейса', content: <PrimitiveDemo /> },
    ]} />
  </>;
}

function PrimitiveDemo() {
  return <div className="primitive-demo">
    <Section title="Статусы задач"><div className="inline-group">
      {(Object.keys(statusPresentation) as ProblemWorkStatus[]).map(status => <StatusBadge key={status} status={status} />)}
    </div></Section>
    <Section title="Доступность"><div className="inline-group"><AvailabilityBadge isAvailable />
      <AvailabilityBadge isAvailable={false} /></div></Section>
    <Section title="Прогресс"><ProgressBar value={25} label="Пример отображения прогресса" /></Section>
    <Section title="Состояния интерфейса"><LoadingSkeleton />
      <EmptyState title="Пока пусто" description="Пример пустого состояния." />
      <ErrorState description="Пример ошибки загрузки." />
      <InlineError>Пример сообщения рядом с полем.</InlineError>
      <AsyncButton pending pendingLabel="Сохранение…">Сохранить</AsyncButton>
    </Section>
  </div>;
}

function Placeholder({ title }: { title: string }) {
  const location = useLocation();
  const queueSearch = typeof location.state?.queueSearch === 'string' ? normalizeQueueQuery(new URLSearchParams(location.state.queueSearch)).toString() : '';
  return <><PageHeader title={title} meta={<span>Демонстрация интерфейса</span>} />
    <EmptyState title="Раздел готовится" description="Здесь появится содержимое выбранного раздела."
      action={location.pathname.startsWith('/teacher/reviews/') ? <Link className="text-link" to={`/teacher/reviews${queueSearch ? `?${queueSearch}` : ''}`}>Вернуться к очереди →</Link> : <Link className="text-link" to="/">Вернуться к обзору →</Link>} />
  </>;
}

export function App({ client = browserApiClient }: { client?: ApiClient }) {
  const location = useLocation();
  const previous = useRef(location);
  const match = matchPath('/courses/:courseId', location.pathname);
  useEffect(() => {
    if (previous.current.pathname !== location.pathname) document.getElementById('main-content')?.focus();
    previous.current = location;
  }, [location]);
  function routeId(value: string) {
    try { return decodeURIComponent(value); } catch { return value; }
  }
  const problemMatch = matchPath('/problem-assignments/:assignmentId', location.pathname);
  if (problemMatch?.params.assignmentId) return <ProblemPage key={problemMatch.params.assignmentId}
    assignmentId={routeId(problemMatch.params.assignmentId)} client={client} />;
  if (location.pathname === '/teacher/reviews') return <ReviewQueuePage client={client} />;
  const courseId = match?.params.courseId ? routeId(match.params.courseId) : undefined;
  const lessonMatch = matchPath('/courses/:courseId/lessons/:lessonId', location.pathname);
  if (lessonMatch?.params.courseId && lessonMatch.params.lessonId) return <LessonPage
    courseId={routeId(lessonMatch.params.courseId)} lessonId={routeId(lessonMatch.params.lessonId)} client={client} />;
  if ((import.meta.env.DEV || window.lmsAttachmentHarness === true) && location.pathname === '/dev/attachments')
    return <main id="main-content"><h1>Тест загрузки фотографий · U1</h1><AttachmentUploader api={attachmentApi(client)} /></main>;
  return courseId
    ? <CoursePage key={courseId} courseId={courseId} client={client} />
    : <DemoApp />;
}

function DemoApp() {
  const location = useLocation();
  const [previewRole, setPreviewRole] = useState<NavigationRole>('Student');
  const role = location.pathname.startsWith('/teacher/') ? 'Teacher' : previewRole;
  const previousLocation = useRef(location);
  useEffect(() => {
    // Preserve initial page focus; announce and focus the main region after navigation.
    const heading = document.querySelector('h1')?.textContent ?? 'Учебное пространство';
    document.title = `${heading} · Sapientum`;
    if (previousLocation.current !== location) document.getElementById('main-content')?.focus();
    previousLocation.current = location;
  }, [location]);
  return <AppShell role={role} context={<>
    <ContextSection title="О пространстве"><p>Учебные материалы и работа над задачами собраны в отдельных разделах.</p>
      <p className="muted">Сейчас доступен предварительный просмотр интерфейса.</p></ContextSection>
    <ContextSection title="Роль в демонстрации"><label htmlFor="preview-role">Навигация</label>
      <select id="preview-role" value={role} disabled={location.pathname.startsWith('/teacher/')}
        onChange={event => setPreviewRole(event.target.value as NavigationRole)}>
        <option value="Student">Ученик</option><option value="Teacher">Преподаватель</option>
      </select><p className="muted small">Меняет только вид меню в демонстрации.</p></ContextSection>
    <ContextSection title="Помощь"><p>Выберите раздел слева или вернитесь к обзору рабочих пространств.</p>
      <Link className="text-link" to="/">К обзору →</Link></ContextSection>
  </>}>
    <Breadcrumbs items={location.pathname === '/' ? [{ label: 'Главная' }]
      : [{ label: 'Главная', to: '/' }, { label: 'Предварительный просмотр' }]} />
    <div className="workspace-content"><Routes>
      <Route path="/" element={<ShellDemo />} />
      <Route path="/teacher/reviews/:problemWorkId" element={<Placeholder title="Проверка решения" />} />
      {[
        ['/courses', 'Мои курсы'], ['/schedule', 'Расписание'], ['/tasks', 'Текущие задачи'],
        ['/archive', 'Архив задач'], ['/progress', 'Прогресс'], ['/messages', 'Сообщения'],
      ].map(([path, title]) => <Route key={path} path={path} element={<Placeholder title={title!} />} />)}
      <Route path="*" element={<><PageHeader title="Страница не найдена" />
        <EmptyState title="Такого раздела нет" action={<Link to="/">На главную</Link>} /></>} />
    </Routes></div>
  </AppShell>;
}

declare global { interface Window { lmsAttachmentHarness?: boolean } }
