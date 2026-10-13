import { array, boolean, count, nullable, object, string, timestamp } from './validation';
import type { ProblemWorkStatus } from '../../components/StatusBadge';

export type ProgressAssignment = {
  assignmentId: string; problemWorkId: string | null; courseVersionId: string;
  courseId: string; courseTitle: string | null; lessonId: string; lessonTitle: string | null;
  problemId: string; problemTitle: string | null; status: ProblemWorkStatus;
  attemptCount: number; isActive: boolean; lastActivityAt: string | null;
};
export type ProgressSummary = {
  studentCount: number; assignedCount: number; notStarted: number;
  submitted: number; needsRevision: number; accepted: number;
};
export type StudentProgressRow = {
  studentId: string; studentDisplayName: string | null; courseId: string; courseTitle: string | null;
  groupId: string | null; groupTitle: string | null; assignedCount: number;
  assignments: ProgressAssignment[];
};
export type TeacherProgress = {
  items: StudentProgressRow[]; page: number; pageSize: number; total: number; summary: ProgressSummary;
};
export type StudentProgress = {
  items: ProgressAssignment[]; page: number; pageSize: number; total: number; summary: ProgressSummary;
};
export type ReviewHistoryItem = {
  reviewId: string; problemWorkId: string; submissionId: string; studentId: string;
  studentDisplayName: string | null; courseId: string; courseTitle: string | null;
  lessonId: string; lessonTitle: string | null; groupId: string | null; groupTitle: string | null;
  problemId: string; problemTitle: string | null; courseVersionId: string; attemptNumber: number;
  attachmentCount: number; decision: 'NeedsRevision' | 'Accepted';
  workStatus: ProblemWorkStatus; createdAt: string;
};
export type ReviewHistory = {
  items: ReviewHistoryItem[]; page: number; pageSize: number; total: number;
  summary: { total: number; needsRevision: number; accepted: number };
};

const positive = (value: unknown) => count(value) && Number.isSafeInteger(value) && (value as number) > 0;
const status = (value: unknown): value is ProblemWorkStatus =>
  typeof value === 'string' && ['NotStarted', 'Submitted', 'NeedsRevision', 'Accepted'].includes(value);
const decision = (value: unknown): value is 'NeedsRevision' | 'Accepted' =>
  value === 'NeedsRevision' || value === 'Accepted';
const assignment = object({ assignmentId: string, problemWorkId: nullable(string), courseVersionId: string,
  courseId: string, courseTitle: nullable(string), lessonId: string, lessonTitle: nullable(string),
  problemId: string, problemTitle: nullable(string), status, attemptCount: count, isActive: boolean,
  lastActivityAt: nullable(timestamp) });
const summary = object({ studentCount: count, assignedCount: count, notStarted: count,
  submitted: count, needsRevision: count, accepted: count });
const teacher = object({ items: array(object({ studentId: string, studentDisplayName: nullable(string),
  courseId: string, courseTitle: nullable(string), groupId: nullable(string), groupTitle: nullable(string),
  assignedCount: count, assignments: array(assignment) })), page: positive, pageSize: positive,
  total: count, summary });
const student = object({ items: array(assignment), page: positive, pageSize: positive, total: count, summary });
const reviews = object({ items: array(object({ reviewId: string, problemWorkId: string, submissionId: string,
  studentId: string, studentDisplayName: nullable(string), courseId: string, courseTitle: nullable(string),
  lessonId: string, lessonTitle: nullable(string), groupId: nullable(string), groupTitle: nullable(string),
  problemId: string, problemTitle: nullable(string), courseVersionId: string, attemptNumber: positive,
  attachmentCount: count, decision, workStatus: status, createdAt: timestamp })),
  page: positive, pageSize: positive, total: count,
  summary: object({ total: count, needsRevision: count, accepted: count }) });

export function decodeTeacherProgress(value: unknown): TeacherProgress {
  if (!teacher(value)) throw new Error('Invalid TeacherProgress');
  return value as TeacherProgress;
}
export function decodeStudentProgress(value: unknown): StudentProgress {
  if (!student(value)) throw new Error('Invalid StudentProgress');
  return value as StudentProgress;
}
export function decodeReviewHistory(value: unknown): ReviewHistory {
  if (!reviews(value)) throw new Error('Invalid ReviewHistory');
  return value as ReviewHistory;
}
