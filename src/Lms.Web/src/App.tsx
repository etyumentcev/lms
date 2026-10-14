import { ProtectedWorkPage } from './features/protected-work/ProtectedWorkPage';
import { useEffect, useRef } from 'react';
import { Link, useLocation, matchPath } from 'react-router-dom';
import { EmptyState } from './components/primitives';
import { ProblemPage, ProblemContentPage } from './features/problem-workspace/ProblemPage';
import { LessonPage, LessonContentPage } from './features/lesson/LessonPage';
import { TeacherReviewPage } from './features/teacher-review/TeacherReviewPage';
import { ReviewQueuePage } from './features/review-queue/ReviewQueuePage';
import { ProgressPage, ReviewHistoryPage } from './features/progress/ProgressPage';
import { CoursesPage } from './features/course/CoursesPage';
import { LearningCatalog } from './features/course/LearningCatalog';
import { useSession } from './auth/session';
import { CoursePage, CourseContentPage } from './features/course/CoursePage';
import { browserApiClient } from './api/browserClient';
import { AttachmentUploader } from './components/attachments/AttachmentUploader';
import { StudentPublishedCoursePage, StudentPublishedLessonPage } from './features/content/StudentStudyPage';
import { attachmentApi } from './components/attachments/api';
import type { ApiClient } from './api/client';

export function App({ client = browserApiClient, contentReads = false }: { client?: ApiClient; contentReads?: boolean }) {
  const location = useLocation();
  const { state: sessionState } = useSession();
  const previous = useRef(location);
  const match = matchPath('/courses/:courseId', location.pathname);
  useEffect(() => {
    if (previous.current.pathname !== location.pathname) document.getElementById('main-content')?.focus();
    previous.current = location;
  }, [location]);
  function routeId(value: string) {
    try { return decodeURIComponent(value); } catch { return value; }
  }
  // Only the separately built test host opts in until production HTTP cutover is authorized.
  if (contentReads) {
    const path = `/api/v1${location.pathname}`;
    if (matchPath('/courses/:courseId/versions/:versionId/lessons/:lessonId/works/:workId', location.pathname))
      return <ProtectedWorkPage key={path} path={path} detail client={client} />;
    if (matchPath('/courses/:courseId/versions/:versionId/lessons/:lessonId/works', location.pathname) ||
        matchPath('/courses/:courseId/versions/:versionId/lessons/:lessonId/problems/:problemId/works', location.pathname))
      return <ProtectedWorkPage key={path} path={path} detail={false} client={client} />;
    if (location.pathname === '/courses') return <CoursesPage client={client} />;
    if (matchPath('/courses/:courseId/versions/:versionId/lessons/:lessonId/problems/:problemId', location.pathname))
      return <ProblemContentPage key={path} path={path} client={client} />;
    if (matchPath('/courses/:courseId/versions/:versionId/lessons/:lessonId', location.pathname))
      return <LessonContentPage key={path} path={path} client={client} />;
    if (match) return <CourseContentPage key={path} path={path} client={client} />;
    return <main id="main-content"><EmptyState title="Раздел недоступен" action={<Link to="/courses">Мои курсы</Link>} /></main>;
  }
  if (sessionState.phase === 'authenticated' && sessionState.session.hasEducationalScope &&
      (location.pathname === '/' || location.pathname === '/courses') && sessionState.session.role)
    return <LearningCatalog role={sessionState.session.role} />;
  const publishedLesson = matchPath('/study/courses/:courseId/versions/:versionId/lessons/:lessonId', location.pathname);
  if (publishedLesson?.params.courseId && publishedLesson.params.versionId && publishedLesson.params.lessonId)
    return <StudentPublishedLessonPage courseId={routeId(publishedLesson.params.courseId)} versionId={routeId(publishedLesson.params.versionId)} lessonId={routeId(publishedLesson.params.lessonId)} client={client}/>;
  const publishedCourse = matchPath('/study/courses/:courseId', location.pathname);
  if (publishedCourse?.params.courseId) return <StudentPublishedCoursePage courseId={routeId(publishedCourse.params.courseId)} client={client}/>;
  const problemMatch = matchPath('/problem-assignments/:assignmentId', location.pathname);
  if (problemMatch?.params.assignmentId) return <ProblemPage key={problemMatch.params.assignmentId}
    assignmentId={routeId(problemMatch.params.assignmentId)} client={client} />;
  if (location.pathname === '/progress' && sessionState.phase === 'authenticated' && sessionState.session.role)
    return <ProgressPage client={client} role={sessionState.session.role} />;
  if (location.pathname === '/teacher/review-history') return <ReviewHistoryPage client={client} />;
  if (location.pathname === '/teacher/reviews') return <ReviewQueuePage client={client} />;
  const reviewMatch = matchPath('/teacher/reviews/:problemWorkId', location.pathname);
  if (reviewMatch?.params.problemWorkId) return <TeacherReviewPage key={reviewMatch.params.problemWorkId}
    problemWorkId={routeId(reviewMatch.params.problemWorkId)} client={client} />;
  if (location.pathname === '/courses') return <CoursesPage client={client} />;
  const assignmentMatch = matchPath('/courses/:courseId/lessons/:lessonId/assignments/:assignmentId', location.pathname);
  if (assignmentMatch?.params.courseId && assignmentMatch.params.lessonId && assignmentMatch.params.assignmentId) return <LessonPage key={location.pathname}
    courseId={routeId(assignmentMatch.params.courseId)} lessonId={routeId(assignmentMatch.params.lessonId)} assignmentId={routeId(assignmentMatch.params.assignmentId)} client={client} />;
  const courseId = match?.params.courseId ? routeId(match.params.courseId) : undefined;
  const lessonMatch = matchPath('/courses/:courseId/lessons/:lessonId', location.pathname);
  if (lessonMatch?.params.courseId && lessonMatch.params.lessonId) return <LessonPage key={location.pathname}
    courseId={routeId(lessonMatch.params.courseId)} lessonId={routeId(lessonMatch.params.lessonId)} client={client} />;
  if (import.meta.env.DEV && location.pathname === '/dev/attachments')
    return <main id="main-content"><h1>Тест загрузки фотографий · U1</h1><AttachmentUploader api={attachmentApi(client)} /></main>;
  return courseId
    ? <CoursePage key={courseId} courseId={courseId} client={client} />
    : <main id="main-content"><EmptyState title="Страница не найдена" action={<Link to="/">На главную</Link>} /></main>;
}
