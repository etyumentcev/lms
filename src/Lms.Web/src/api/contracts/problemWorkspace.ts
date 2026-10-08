import type { LessonWorkspace, Submission, Review, Message } from './lessonWorkspace';
import { course, lesson, problem, availability, work, permissions, material, submission, review, message } from './lessonWorkspace';
import { object, array, string, boolean, count, nullable } from './validation';
type Row = LessonWorkspace['problems'][number];
export type ProblemWorkspace = {
  assignment: { id: string; isActive: boolean }; course: LessonWorkspace['course']; lesson: LessonWorkspace['lesson'];
  problem: Row['problem']; availability: Row['availability']; work: Row['work']; permissions: Row['permissions'];
  hints: { id: string; order: number; title: string | null; bodyMarkdown: string }[];
  history: { submissions: Submission[]; reviews: Review[]; messages: Message[] };
  usefulMaterials: LessonWorkspace['materials'];
};
const workspace = object({ assignment: object({ id: string, isActive: boolean }), course, lesson, problem, availability, work, permissions,
  hints: array(object({ id: string, order: count, title: nullable(string), bodyMarkdown: string })),
  history: object({ submissions: array(submission), reviews: array(review), messages: array(message) }), usefulMaterials: array(material) });
export function decodeProblemWorkspace(value: unknown): ProblemWorkspace {
  if (!workspace(value)) throw Error('Invalid ProblemWorkspace');
  return value as ProblemWorkspace;
}
export type SubmitSolutionRequest = { bodyMarkdown: string | null; attachmentIds: string[]; expectedVersion: number | null };
