/** UUID v4 also works on local-network HTTP, where randomUUID may be unavailable. */
export function strokeId(): string {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
export type StrokePoint = { x: number; y: number; pressure?: number | null };
export type PenStroke = { id: string; tool: 'pen'; color: string; width: number; points: StrokePoint[] };
export type AnnotatedAttachment = { attachmentId: string; strokes: PenStroke[] };
export type PageState = { strokes: PenStroke[]; undo: PenStroke[][]; redo: PenStroke[][]; zoom: number; x: number; y: number };
export const newPage = (strokes: PenStroke[] = []): PageState => ({ strokes, undo: [], redo: [], zoom: 1, x: 0, y: 0 });
export function normalizedPoint(clientX: number, clientY: number, rect: Pick<DOMRect, 'left' | 'top' | 'width' | 'height'>): StrokePoint | null {
  const x = (clientX - rect.left) / rect.width, y = (clientY - rect.top) / rect.height;
  return Number.isFinite(x) && Number.isFinite(y) && x >= 0 && x <= 1 && y >= 0 && y <= 1 ? { x, y } : null;
}
export function edit(page: PageState, strokes: PenStroke[]): PageState {
  return { ...page, strokes, undo: [...page.undo.slice(-49), page.strokes], redo: [] };
}
export function undo(page: PageState): PageState {
  return page.undo.length ? { ...page, strokes: page.undo.at(-1)!, undo: page.undo.slice(0, -1), redo: [...page.redo, page.strokes] } : page;
}
export function redo(page: PageState): PageState {
  return page.redo.length ? { ...page, strokes: page.redo.at(-1)!, redo: page.redo.slice(0, -1), undo: [...page.undo, page.strokes] } : page;
}
export function hit(stroke: PenStroke, point: StrokePoint, width: number, height: number) {
  const radius = Math.max(8, stroke.width * Math.min(width, height));
  return stroke.points.some((a, i) => {
    const b = stroke.points[i + 1] ?? a;
    const dx = (b.x - a.x) * width, dy = (b.y - a.y) * height;
    const px = (point.x - a.x) * width, py = (point.y - a.y) * height;
    const t = Math.max(0, Math.min(1, (px * dx + py * dy) / (dx * dx + dy * dy || 1)));
    return Math.hypot(px - t * dx, py - t * dy) <= radius;
  });
}
export function validAnnotations(value: unknown): boolean {
  if (!Array.isArray(value) || value.length > 20) return false;
  const pages = new Set<string>(), ids = new Set<string>(); let count = 0;
  return value.every(page => {
    if (!page || typeof page.attachmentId !== 'string' || pages.has(page.attachmentId) || !Array.isArray(page.strokes)) return false;
    pages.add(page.attachmentId);
    return page.strokes.every((s: PenStroke) => {
      if (!s || typeof s.id !== 'string' || ids.has(s.id) || s.tool !== 'pen' || !/^#[0-9a-fA-F]{6}$/.test(s.color) ||
        !Number.isFinite(s.width) || s.width < .0005 || s.width > .05 || !Array.isArray(s.points) || !s.points.length || s.points.length > 4096) return false;
      ids.add(s.id); count += s.points.length;
      return ids.size <= 2000 && count <= 100000 && s.points.every(p => p && [p.x, p.y, p.pressure ?? .5].every(n => typeof n === 'number' && Number.isFinite(n) && n >= 0 && n <= 1));
    });
  });
}
