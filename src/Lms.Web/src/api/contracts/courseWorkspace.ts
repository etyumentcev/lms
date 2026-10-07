/** C3/C4 wire contract: src/Lms.Application/ReadModels.cs. One frontend definition. */
export type CourseWorkspace = {
  course: { id: string; title: string | null; publishedVersionId: string | null;
    description: string | null; gradeLabel: string | null;
    teacher: { id: string; displayName: string | null; avatarUrl: string | null } | null };
  enrollment: { id: string | null; studentId: string; courseId: string; groupId: string | null; status: string } | null;
  teacherContext: { teacherId: string; canReviewCourse: boolean; groupIds: (string | null)[] } | null;
  progress: { completedLessons: number; availableLessons: number; totalLessons: number; percent: number };
  lessons: CourseLessonRow[];
  materials: { id: string; title: string; kind: string; description: string | null; url: string }[];
};
export type CourseLessonRow = {
  lesson: { id: string; courseVersionId: string; order: number; title: string | null; summary: string | null };
  availability: { isAvailable: boolean; availableFrom: string | null; reason: string };
  progress: { status: 'NotStarted' | 'InProgress' | 'Completed'; acceptedProblems: number; totalAssignedProblems: number; percent: number };
  courseVersionIds: string[];
};

// Validate wire shape at the trust boundary; no progress/availability business calculations.
type Check = (value: unknown) => boolean;
const string: Check = value => typeof value === 'string';
const boolean: Check = value => typeof value === 'boolean';
const count: Check = value => typeof value === 'number' && Number.isInteger(value) && value >= 0;
const percent: Check = value => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 100;
const nullable = (check: Check): Check => value => value === null || check(value);
const array = (check: Check): Check => value => Array.isArray(value) && value.every(check);
const object = (shape: Record<string, Check>): Check => value => value !== null && typeof value === 'object'
  && !Array.isArray(value) && Object.entries(shape).every(([key, check]) => check((value as Record<string, unknown>)[key]));
const timestamp: Check = value => typeof value === 'string' && Number.isFinite(Date.parse(value));
const lesson = object({
  lesson: object({ id: string, courseVersionId: string, order: count, title: nullable(string), summary: nullable(string) }),
  availability: object({ isAvailable: boolean, availableFrom: nullable(timestamp), reason: string }),
  progress: object({ status: value => typeof value === 'string' && ['NotStarted', 'InProgress', 'Completed'].includes(value),
    acceptedProblems: count, totalAssignedProblems: count, percent }),
  courseVersionIds: array(string),
});
const workspace = object({
  course: object({ id: string, title: nullable(string), publishedVersionId: nullable(string), description: nullable(string),
    gradeLabel: nullable(string), teacher: nullable(object({ id: string, displayName: nullable(string), avatarUrl: nullable(string) })) }),
  enrollment: nullable(object({ id: nullable(string), studentId: string, courseId: string, groupId: nullable(string), status: string })),
  teacherContext: nullable(object({ teacherId: string, canReviewCourse: boolean, groupIds: array(nullable(string)) })),
  progress: object({ completedLessons: count, availableLessons: count, totalLessons: count, percent }),
  lessons: array(lesson),
  materials: array(object({ id: string, title: string, kind: string, description: nullable(string), url: string })),
});
export function decodeCourseWorkspace(value: unknown): CourseWorkspace {
  if (!workspace(value)) throw new Error('Invalid CourseWorkspace');
  return value as CourseWorkspace;
}
