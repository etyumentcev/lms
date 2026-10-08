import type { AnnotatedAttachment } from '../../components/annotations/model';
import type { LessonWorkspace, Submission, Review, Message } from './lessonWorkspace';
import { course, lesson, problem, permissions, user, submission, review, message, oneOf } from './lessonWorkspace';
import { object, array, string, count, nullable } from './validation';
import type { ProblemWorkStatus } from '../../components/StatusBadge';
type Row = LessonWorkspace['problems'][number];
/** Exact C4 ProblemWorkDetailsDto. Reference solutions/criteria have no wire fields. */
export type ProblemWorkDetails = {
  id: string; problemAssignmentId: string; status: ProblemWorkStatus; version: number; attemptCount: number;
  student: { id: string; displayName: string | null; avatarUrl: string | null };
  course: LessonWorkspace['course']; lesson: LessonWorkspace['lesson']; problem: Row['problem'];
  currentSubmission: Submission | null; submissions: Submission[]; reviews: Review[]; messages: Message[];
  permissions: Row['permissions'];
};
const details = object({ id: string, problemAssignmentId: string,
  status: oneOf('NotStarted', 'Submitted', 'NeedsRevision', 'Accepted'),
  version: value => count(value) && Number.isSafeInteger(value), attemptCount: count,
  student: user, course, lesson, problem, currentSubmission: nullable(submission),
  submissions: array(submission), reviews: array(review), messages: array(message), permissions });
export function decodeProblemWork(value: unknown): ProblemWorkDetails {
  if (!details(value)) throw Error('Invalid ProblemWorkDetails');
  return value as ProblemWorkDetails;
}
export type ReviewRequest = { submissionId: string; feedbackMarkdown: string | null; expectedVersion: number; annotations?: AnnotatedAttachment[] };
