import { array, count, nullable, object, string } from './validation';

export type ContentLink = { id: string; versionId: string; order: number; title: string | null; href: string };
export type CourseContent = { id: string; title: string | null; description: string | null; lessons: ContentLink[] };
export type LessonContent = { courseId: string; versionId: string; id: string; order: number; title: string | null; summary: string | null; problems: ContentLink[] };
export type ProblemContent = { courseId: string; versionId: string; lessonId: string; id: string; order: number; title: string | null; statementMarkdown: string };
export type Content = CourseContent | LessonContent | ProblemContent;
export type ContentKind = 'course' | 'lesson' | 'problem';
const uuid = (v: unknown) => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v);
// Exact shapes deliberately reject accidental mixing with personal workspace DTOs.
const exact = (shape: Parameters<typeof object>[0]) => (v: unknown) => object(shape)(v)
  && Object.keys(v as object).length === Object.keys(shape).length;
const link = exact({ id: string, versionId: uuid, order: count, title: nullable(string), href: string });
const validators = {
  course: exact({ id: string, title: nullable(string), description: nullable(string), lessons: array(link) }),
  lesson: exact({ courseId: string, versionId: uuid, id: string, order: count, title: nullable(string), summary: nullable(string), problems: array(link) }),
  problem: exact({ courseId: string, versionId: uuid, lessonId: string, id: string, order: count, title: nullable(string), statementMarkdown: string }),
};
const part = encodeURIComponent;
export function contentApiPath(data: Content): string {
  if ('lessons' in data) return `/api/v1/courses/${part(data.id)}`;
  const parent = `/api/v1/courses/${part(data.courseId)}/versions/${part(data.versionId)}/lessons/`;
  return 'problems' in data ? parent + part(data.id) : `${parent}${part(data.lessonId)}/problems/${part(data.id)}`;
}
export function decodeContent(kind: ContentKind, expectedPath: string) {
  return (value: unknown): Content => {
    if (!validators[kind](value)) throw new Error('Invalid content');
    const data = value as Content;
    if (contentApiPath(data) !== expectedPath) throw new Error('Mismatched content parent');
    const links = 'lessons' in data ? data.lessons : 'problems' in data ? data.problems : [];
    for (const item of links) {
      const expected = 'lessons' in data
        ? `${expectedPath}/versions/${part(item.versionId)}/lessons/${part(item.id)}`
        : `${expectedPath}/problems/${part(item.id)}`;
      if (item.href !== expected || ('versionId' in data && item.versionId !== data.versionId)) throw new Error('Invalid content link');
    }
    return data;
  };
}
// Only use after parent-aware decoding. API links become same-origin UI routes.
export function contentUiPath(href: string) { return href.slice('/api/v1'.length); }
