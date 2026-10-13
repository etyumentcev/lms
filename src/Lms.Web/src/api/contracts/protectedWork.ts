import { array, count, nullable, object, string } from './validation';
const uuid = (v: unknown) => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v);
const exact = (shape: Parameters<typeof object>[0]) => (v: unknown) => object(shape)(v) && Object.keys(v as object).length === Object.keys(shape).length;
const status = (v: unknown) => ['NotStarted', 'Submitted', 'NeedsRevision', 'Accepted'].includes(v as string);
type Review = { id: string; decision: string; feedbackMarkdown: string | null; createdAt: string };
export type Attempt = { id: string; number: number; bodyMarkdown: string | null; submittedAt: string; photoCount: number; photoAvailability: 'unavailable' | 'protected'; reviews: Review[] };
export type ProtectedWork = { id: string; status: string; statementHref: string; attempts: Attempt[]; messages: { id: string; role: string; bodyMarkdown: string; createdAt: string; photoCount: number }[] };
export type WorkList = { items: { id: string; problemId: string; status: string; href: string }[] };
const review = exact({ id: uuid, decision: (v: unknown) => ['Accepted', 'NeedsRevision'].includes(v as string), feedbackMarkdown: nullable(string), createdAt: string });
const attempt = exact({ id: uuid, number: count, bodyMarkdown: nullable(string), submittedAt: string, photoCount: count, photoAvailability: (v: unknown) => (v === 'unavailable' || v === 'protected'), reviews: array(review) });
const message = exact({ id: uuid, role: (v: unknown) => ['Student', 'Teacher'].includes(v as string), bodyMarkdown: string, createdAt: string, photoCount: count });
export function decodeWork(path: string, detail: boolean) {
  return (value: unknown): ProtectedWork | WorkList => {
    const root = path.split('/problems/')[0]!.split('/works')[0]!;
    if (detail) {
      if (!exact({ id: uuid, status, statementHref: string, attempts: array(attempt), messages: array(message) })(value)) throw new Error('Invalid work');
      const work = value as ProtectedWork;
      if (path !== `${root}/works/${work.id}` || !work.statementHref.startsWith(`${root}/problems/`) ||
        !/^[-\w%]+$/.test(work.statementHref.slice(`${root}/problems/`.length))) throw new Error('Invalid parent');
      if (new Set(work.attempts.map(a => a.id)).size !== work.attempts.length || work.attempts.some((a, i) => a.number !== i + 1)) throw new Error('Invalid attempts');
      return work;
    }
    if (!exact({ items: array(exact({ id: uuid, problemId: string, status, href: string })) })(value)) throw new Error('Invalid work list');
    const list = value as WorkList;
    for (const row of list.items) if (row.href !== `${root}/works/${row.id}`) throw new Error('Invalid work link');
    return list;
  };
}
