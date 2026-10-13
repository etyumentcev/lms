/** C4 wire projection. Queue membership and aggregates belong to the server. */
import { array, count, nullable, object, string, timestamp } from './validation';
export type ReviewQueueItem = {
  problemWorkId: string; submissionId: string; studentId: string; studentDisplayName: string | null;
  courseId: string; courseTitle: string | null; lessonId: string; lessonTitle: string | null;
  groupId: string | null; groupTitle: string | null; problemId: string; problemTitle: string | null;
  attemptNumber: number; submittedAt: string; waitingDurationSeconds: number; version: number;
};
export type ReviewQueue = {
  items: ReviewQueueItem[]; page: number; pageSize: number; total: number;
  summary: { total: number; firstAttempts: number; revisions: number; averageWaitingSeconds: number | null;
    byCourse: { courseId: string; courseTitle: string | null; count: number }[] };
};
const positive = (value: unknown) => count(value) && Number.isSafeInteger(value) && (value as number) > 0;
const seconds = (value: unknown) => typeof value === 'number' && Number.isFinite(value);
const item = object({ problemWorkId: string, submissionId: string, studentId: string, studentDisplayName: nullable(string),
  courseId: string, courseTitle: nullable(string), lessonId: string, lessonTitle: nullable(string),
  groupId: nullable(string), groupTitle: nullable(string), problemId: string, problemTitle: nullable(string),
  attemptNumber: positive, submittedAt: timestamp, waitingDurationSeconds: seconds, version: positive });
const queue = object({ items: array(item), page: positive, pageSize: positive, total: count,
  summary: object({ total: count, firstAttempts: count, revisions: count, averageWaitingSeconds: nullable(seconds),
    byCourse: array(object({ courseId: string, courseTitle: nullable(string), count })) }) });
export function decodeReviewQueue(value: unknown): ReviewQueue {
  if (!queue(value)) throw new Error('Invalid ReviewQueue');
  return value as ReviewQueue;
}
