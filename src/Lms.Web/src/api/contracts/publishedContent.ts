import { array, count, nullable, object, string } from './validation';

export type PublishedHint = { id: string; title?: string; order: number; bodyMarkdown: string };
export type PublishedProblem = { id: string; title?: string; source?: string; order: number; bodyMarkdown: string; hints: PublishedHint[] };
export type PublishedLesson = { id: string; title: string; order: number; bodyMarkdown: string; problems: PublishedProblem[] };
export type PublishedAsset = { id: string; ownerKind: 'course'|'lesson'|'problem'; ownerId: string; visibility: 'student';
  mediaType: string; sizeBytes: number; sha256: string; artifactPath: string };
export type PublishedStudentContent = { artifactSchema: 'student-content/v1'; contractRevision: string;
  studentProjectionHash: string; course: { id: string; title: string; description?: string; bodyMarkdown: string;
    lessons: PublishedLesson[] }; assets: PublishedAsset[] };
export type StudentAssignment = { assignmentId: string; contentVersionId: string; versionNumber: number;
  lessonId: string; lessonTitle: string; problemId: string; problemTitle: string|null; problemOrder: number;
  status: 'NotStarted'|'Submitted'|'NeedsRevision'|'Accepted'; attemptCount: number };

const optionalString = (value: unknown) => value === undefined || string(value);
const exact = (shape: Parameters<typeof object>[0]) => (value: unknown) => object(shape)(value)
  && Object.keys(value as object).length === Object.keys(shape).length;
const hint = object({ id: string, title: optionalString, order: count, bodyMarkdown: string });
const problem = object({ id: string, title: optionalString, source: optionalString, order: count, bodyMarkdown: string, hints: array(hint) });
const lesson = object({ id: string, title: string, order: count, bodyMarkdown: string, problems: array(problem) });
const asset = exact({ id: string, ownerKind: (v:unknown)=>['course','lesson','problem'].includes(String(v)), ownerId: string,
  visibility: (v:unknown)=>v==='student', mediaType: string, sizeBytes: count, sha256: string, artifactPath: string });
const course = object({ id: string, title: string, description: optionalString, bodyMarkdown: string, lessons: array(lesson) });
const artifact = exact({ artifactSchema:(v:unknown)=>v==='student-content/v1', contractRevision:string,
  course, assets:array(asset), studentProjectionHash:string });
const assignment = exact({ assignmentId:string, contentVersionId:string, versionNumber:count, lessonId:string,
  lessonTitle:string, problemId:string, problemTitle:nullable(string), problemOrder:count,
  status:(v:unknown)=>['NotStarted','Submitted','NeedsRevision','Accepted'].includes(String(v)), attemptCount:count });

export function decodePublishedStudentContent(value: unknown): PublishedStudentContent {
  if (!artifact(value)) throw new Error('Invalid student content projection');
  const data = value as PublishedStudentContent;
  const paths = new Set<string>();
  for (const item of data.assets) {
    if (!/^assets\/[a-z][a-z0-9-]*\.(png|jpg|jpeg|webp|txt)$/i.test(item.artifactPath) || paths.has(item.artifactPath))
      throw new Error('Invalid student asset manifest');
    paths.add(item.artifactPath);
  }
  return data;
}

export function decodeStudentAssignments(value: unknown): StudentAssignment[] {
  if (!value || typeof value !== 'object' || !Array.isArray((value as {items?:unknown}).items)
    || !(value as {items:unknown[]}).items.every(assignment)) throw new Error('Invalid student assignments');
  return (value as { items: StudentAssignment[] }).items;
}
