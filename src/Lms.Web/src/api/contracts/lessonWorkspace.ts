/** Exact C4 wire contract, including assignment identity and server-derived work/progress. */
import type { CourseWorkspace, CourseLessonRow } from './courseWorkspace';
import type { ProblemWorkStatus } from '../../components/StatusBadge';
import { string, boolean, count, percent, nullable, array, object, timestamp } from './validation';
type Submission = { id: string; attemptNumber: number; bodyMarkdown: string | null; submittedAt: string; attachmentIds: string[] };
type Review = { id: string; submissionId: string; teacherId: string; decision: 'Accepted' | 'NeedsRevision'; messageId: string | null; createdAt: string };
type Message = { id: string; author: { id: string; displayName: string | null; avatarUrl: string | null }; authorRole: 'Student' | 'Teacher'; bodyMarkdown: string; replyToMessageId: string | null; createdAt: string; attachmentIds: string[] };
export type LessonWorkspace = {
  course: CourseWorkspace['course']; lesson: CourseLessonRow['lesson']; contentVersions: CourseLessonRow['lesson'][];
  availability: CourseLessonRow['availability']; progress: CourseLessonRow['progress'];
  problems: { problemAssignmentId: string; studentId: string;
    problem: { id: string; courseVersionId: string; order: number; title: string | null; statementMarkdown: string };
    availability: CourseLessonRow['availability'];
    work: { problemWorkId: string | null; status: ProblemWorkStatus; attemptCount: number; version: number | null;
      latestSubmission: Submission | null; latestReview: Review | null; latestTeacherFeedback: Message | null };
    permissions: { canSubmit: boolean; canPostMessage: boolean; canAccept: boolean; canRequestRevision: boolean };
  }[]; materials: CourseWorkspace['materials'];
};
const oneOf = (...values: string[]) => (value: unknown) => typeof value === 'string' && values.includes(value);
const user = object({ id: string, displayName: nullable(string), avatarUrl: nullable(string) });
const lesson = object({ id: string, courseVersionId: string, order: count, title: nullable(string), summary: nullable(string) });
const availability = object({ isAvailable: boolean, availableFrom: nullable(timestamp), reason: string });
const workspace = object({
  course: object({ id: string, title: nullable(string), publishedVersionId: nullable(string), description: nullable(string), gradeLabel: nullable(string), teacher: nullable(user) }),
  lesson, contentVersions: array(lesson), availability,
  progress: object({ status: oneOf('NotStarted', 'InProgress', 'Completed'), acceptedProblems: count, totalAssignedProblems: count, percent }),
  problems: array(object({ problemAssignmentId: string, studentId: string,
    problem: object({ id: string, courseVersionId: string, order: count, title: nullable(string), statementMarkdown: string }), availability,
    work: object({ problemWorkId: nullable(string), status: oneOf('NotStarted', 'Submitted', 'NeedsRevision', 'Accepted'), attemptCount: count, version: nullable(count),
      latestSubmission: nullable(object({ id: string, attemptNumber: count, bodyMarkdown: nullable(string), submittedAt: timestamp, attachmentIds: array(string) })),
      latestReview: nullable(object({ id: string, submissionId: string, teacherId: string, decision: oneOf('Accepted', 'NeedsRevision'), messageId: nullable(string), createdAt: timestamp })),
      latestTeacherFeedback: nullable(object({ id: string, author: user, authorRole: oneOf('Student', 'Teacher'), bodyMarkdown: string, replyToMessageId: nullable(string), createdAt: timestamp, attachmentIds: array(string) })) }),
    permissions: object({ canSubmit: boolean, canPostMessage: boolean, canAccept: boolean, canRequestRevision: boolean }) })),
  materials: array(object({ id: string, title: string, kind: string, description: nullable(string), url: string })),
});
export function decodeLessonWorkspace(value: unknown): LessonWorkspace {
  if (!workspace(value)) throw new Error('Invalid LessonWorkspace');
  return value as LessonWorkspace;
}
