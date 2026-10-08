import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { Profiler, useState } from 'react';
import { AnnotationPhoto } from '../components/annotations/AnnotationPhoto';
import { strokeId, edit, hit, newPage, normalizedPoint, redo, undo, validAnnotations, type PenStroke } from '../components/annotations/model';
import { SubmissionViewer } from '../features/teacher-review/SubmissionViewer';
const stroke = (): PenStroke => ({ id: crypto.randomUUID(), tool: 'pen', color: '#dc2626', width: .003, points: [{ x: .2, y: .3 }, { x: .8, y: .9 }] });
const api = { content: vi.fn(async () => new Blob(['image'])), upload: vi.fn(), remove: vi.fn() };
beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:original'); URL.revokeObjectURL = vi.fn();
  class Pointer extends MouseEvent { pointerId: number; pointerType: string; pressure: number;
    getCoalescedEvents() { return []; }
    constructor(type: string, init: PointerEventInit) { super(type, init); this.pointerId = init.pointerId ?? 1; this.pointerType = init.pointerType ?? 'mouse'; this.pressure = init.pressure ?? 0; }
  }
  vi.stubGlobal('PointerEvent', Pointer);
});
it.each([[10, 20, 100, 200], [-200, 80, 1000, 2000], [5, 3, 320, 640], [0, 0, 2048, 4096]])('normalizes actual image rectangle at zoom/pan/resize %s %s', (left, top, width, height) => {
  expect(normalizedPoint(left + .2 * width, top + .3 * height, { left, top, width, height })).toEqual({ x: .2, y: .3 });
});
it('rejects contain margins and zero-size geometry', () => { expect(normalizedPoint(9, 20, { left: 10, top: 10, width: 100, height: 100 })).toBeNull(); expect(normalizedPoint(0, 0, { left: 0, top: 0, width: 0, height: 0 })).toBeNull(); });
it('undo redo restores a stroke and a new edit clears redo', () => { const s = stroke(), p = edit(newPage(), [s]); expect(undo(p).strokes).toEqual([]); expect(redo(undo(p)).strokes).toEqual([s]); expect(edit(undo(p), [stroke()]).redo).toEqual([]); });
it('eraser intersects segments rather than just points', () => { expect(hit(stroke(), { x: .5, y: .6 }, 1000, 1000)).toBe(true); expect(hit(stroke(), { x: .9, y: .1 }, 1000, 1000)).toBe(false); });
it('bounds undo memory', () => { let p = newPage(); for (let i = 0; i < 80; i++) p = edit(p, [stroke()]); expect(p.undo).toHaveLength(50); });
it('accepts empty annotations and rejects invalid metadata', () => { expect(validAnnotations([])).toBe(true); expect(validAnnotations([{ attachmentId: 'a', strokes: [{ ...stroke(), color: '<script>' }] }])).toBe(false); });
it.each([NaN, Infinity, -1, 1.1])('rejects invalid coordinates %s', x => { expect(validAnnotations([{ attachmentId: 'a', strokes: [{ ...stroke(), points: [{ x, y: .5 }] }] }])).toBe(false); });
function Editor({ editable = true }: { editable?: boolean }) { const [state, change] = useState(newPage()); return <AnnotationPhoto api={api} id="a" page={1} state={state} change={change} editable={editable} />; }
function canvas() { Object.defineProperties(screen.getByRole('img'), { naturalWidth: { value: 100, configurable: true }, naturalHeight: { value: 200, configurable: true } }); fireEvent.load(screen.getByRole('img')); const c = screen.getByLabelText('Слой пометок страницы 1'); vi.spyOn(c, 'getBoundingClientRect').mockReturnValue({ left: 10, top: 20, width: 100, height: 200 } as DOMRect); return c; }
function draw(type = 'mouse') { const c = canvas(); fireEvent.pointerDown(c, { pointerId: 1, pointerType: type, clientX: 30, clientY: 80, button: 0, pressure: .7 }); fireEvent.pointerMove(c, { pointerId: 1, pointerType: type, clientX: 90, clientY: 180, pressure: .8 }); fireEvent.pointerUp(c, { pointerId: 1, pointerType: type }); }
it.each(['mouse', 'pen'])('draws %s and supports undo/redo/eraser', async type => {
  render(<Editor />); await screen.findByAltText('Страница 1 — фото решения'); draw(type); expect(screen.getByText(/Графические пометки: 1/)).toBeVisible();
  fireEvent.click(screen.getByText('Отмена')); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible(); fireEvent.click(screen.getByText('Повтор')); expect(screen.getByText(/Графические пометки: 1/)).toBeVisible();
  fireEvent.click(screen.getByText('Ластик')); draw(type); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible();
});
it('touch defaults to gestures and opt-in allows drawing', async () => { render(<Editor />); await screen.findByRole('img'); draw('touch'); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible(); fireEvent.click(screen.getByLabelText('Рисовать пальцем')); draw('touch'); expect(screen.getByText(/Графические пометки: 1/)).toBeVisible(); });
it('multiple touches cancel the provisional stroke', async () => { render(<Editor />); await screen.findByRole('img'); fireEvent.click(screen.getByLabelText('Рисовать пальцем')); const c = canvas(); fireEvent.pointerDown(c, { pointerId: 1, pointerType: 'touch', clientX: 30, clientY: 80 }); fireEvent.pointerDown(c, { pointerId: 2, pointerType: 'touch', clientX: 40, clientY: 80 }); fireEvent.pointerUp(c, { pointerId: 1 }); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible(); });
it('pointer cancellation never commits a stroke', async () => { render(<Editor />); await screen.findByRole('img'); const c = canvas(); fireEvent.pointerDown(c, { clientX: 30, clientY: 80 }); fireEvent.pointerCancel(c); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible(); });
it('student has no editing tools and drawing cannot mutate', async () => { render(<Editor editable={false} />); await screen.findByRole('img'); expect(screen.queryByText('Перо')).toBeNull(); draw(); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible(); fireEvent.click(screen.getByLabelText('Показать пометки')); expect(screen.getByLabelText('Показать пометки')).not.toBeChecked(); });
it('color and thickness are accessible controls', async () => { render(<Editor />); await screen.findByRole('img'); fireEvent.change(screen.getByLabelText('Цвет пера'), { target: { value: '#0000ff' } }); fireEvent.change(screen.getByLabelText('Толщина пера'), { target: { value: '0.006' } }); expect(screen.getByLabelText('Цвет пера')).toHaveValue('#0000ff'); expect(screen.getByLabelText('Толщина пера')).toHaveValue('0.006'); });
it('page switching preserves independent strokes, zoom and undo history', async () => {
  render(<SubmissionViewer api={api} editable submission={{ id: 's', attemptNumber: 1, bodyMarkdown: null, submittedAt: '2026-10-08T00:00:00Z', attachmentIds: ['a', 'b'] }} />);
  await screen.findByRole('img'); draw(); fireEvent.click(screen.getByLabelText('Увеличить фото')); fireEvent.click(screen.getByText('Следующая')); await screen.findByAltText('Страница 2 — фото решения'); expect(screen.getByText(/Графические пометки: 0/)).toBeVisible();
  fireEvent.click(screen.getByText('Предыдущая')); await screen.findByAltText('Страница 1 — фото решения'); expect(screen.getByText(/Графические пометки: 1/)).toBeVisible(); expect(screen.getByLabelText('Масштаб')).toHaveTextContent('150%'); expect(screen.getByText('Отмена')).toBeEnabled();
});
it('fullscreen fallback enters, traps focus and exits on Escape', async () => {
  render(<SubmissionViewer api={api} submission={{ id: 's', attemptNumber: 1, bodyMarkdown: null, submittedAt: '2026-10-08T00:00:00Z', attachmentIds: ['a'] }} />);
  fireEvent.click(screen.getByText('На весь экран')); expect(screen.getByRole('dialog')).toHaveAttribute('aria-modal', 'true'); fireEvent.keyDown(document, { key: 'Escape' }); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
});

it('empty coalesced event arrays still record the pointermove sample', async () => {
  const change = vi.fn(); render(<AnnotationPhoto api={api} id="a" page={1} state={newPage()} change={change} editable />);
  await screen.findByRole('img'); draw('pen'); expect(change.mock.calls[0]![0].strokes[0].points).toHaveLength(2);
});

it('image decode failure exposes retry without an intercepting canvas', async () => {
  render(<Editor />); await screen.findByRole('img'); canvas(); fireEvent.error(screen.getByRole('img'));
  expect(screen.getByText('Повторить предпросмотр страницы 1')).toBeVisible(); expect(screen.getByLabelText('Слой пометок страницы 1')).not.toBeVisible();
});

it('creates valid unique stroke UUIDs without secure-context randomUUID', () => {
  const randomUUID = vi.spyOn(crypto, 'randomUUID').mockImplementation(undefined!);
  Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true });
  try {
    const ids = Array.from({ length: 100 }, strokeId);
    expect(new Set(ids).size).toBe(100);
    for (const id of ids) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  } finally { randomUUID.mockRestore(); }
});
it('pen mode reserves the drawing surface before contact; Hand permits browser pinch', async () => {
  render(<Editor />); await screen.findByRole('img');
  expect(canvas().style.touchAction).toBe('none');
  fireEvent.click(screen.getByText('Ластик')); expect(canvas().style.touchAction).toBe('none');
  fireEvent.click(screen.getByText('Рука')); expect(canvas().style.touchAction).toBe('pinch-zoom');
});
it('stylus draws when randomUUID is unavailable', async () => {
  const randomUUID = vi.spyOn(crypto, 'randomUUID');
  Object.defineProperty(crypto, 'randomUUID', { value: undefined, configurable: true });
  try { render(<Editor />); await screen.findByRole('img'); draw('pen'); expect(screen.getByText(/Графические пометки: 1/)).toBeVisible(); }
  finally { randomUUID.mockRestore(); }
});

it('records a long stroke without React commits for each point', async () => {
  const commits = vi.fn(); render(<Profiler id="drawing" onRender={commits}><Editor /></Profiler>);
  await screen.findByRole('img'); const c = canvas(); const before = commits.mock.calls.length;
  fireEvent.pointerDown(c, { pointerId: 7, pointerType: 'pen', clientX: 30, clientY: 80, button: 0 });
  for (let n = 0; n < 100; n++) fireEvent.pointerMove(c, { pointerId: 7, pointerType: 'pen', clientX: 30 + n / 2, clientY: 80 });
  expect(commits.mock.calls.length).toBe(before);
  fireEvent.pointerUp(c, { pointerId: 7, pointerType: 'pen' });
  expect(commits.mock.calls.length).toBeGreaterThan(before);
  expect(screen.getByText(/Графические пометки: 1/)).toBeVisible();
});
