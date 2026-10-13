import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { PointerEvent as ReactPointerEvent } from 'react';
import { AttachmentPreview } from '../attachments/AttachmentPreview';
import type { AttachmentApi } from '../attachments/api';
import { strokeId, edit, hit, normalizedPoint, redo, undo, type PageState, type PenStroke } from './model';
import './annotations.css';
type Tool = 'pen' | 'eraser' | 'hand';
export function AnnotationPhoto({ api, id, page, state, change, editable = false }: {
  api: AttachmentApi; id: string; page: number; state: PageState; change: (next: PageState) => void; editable?: boolean;
}) {
  const [tool, setTool] = useState<Tool>('pen'), [color, setColor] = useState('#dc2626'), [width, setWidth] = useState(.003);
  const [visible, setVisible] = useState(true), [touchPen, setTouchPen] = useState(false);
  const [notice, setNotice] = useState('');
  const [ratio, setRatio] = useState(1), [ready, setReady] = useState(false);
  const layer = useRef<HTMLCanvasElement>(null), photo = useRef<HTMLDivElement>(null);
  const current = useRef(state); current.current = state;
  const gesture = useRef<{ id: number; x: number; y: number; stroke?: PenStroke; before: PageState } | null>(null);
  const pointers = useRef(new Set<number>()), penSeen = useRef(false);
  const frame = useRef(0);
  const draw = () => {
    const canvas = layer.current, image = photo.current?.querySelector('img');
    if (!canvas || !image || !image.complete || !image.naturalWidth) return;
    const w = image.clientWidth, h = image.clientHeight, dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
    const ctx = canvas.getContext('2d'); if (!ctx) return;
    ctx.scale(dpr, dpr); ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    if (!visible) return;
    for (const stroke of [...current.current.strokes, ...(gesture.current?.stroke ? [gesture.current.stroke] : [])]) {
      ctx.strokeStyle = stroke.color; ctx.fillStyle = stroke.color;
      stroke.points.forEach((p, i) => {
        const a = stroke.points[Math.max(0, i - 1)]!;
        const thickness = stroke.width * Math.min(w, h) * (.5 + (p.pressure ?? .5));
        ctx.lineWidth = thickness;
        ctx.beginPath(); ctx.moveTo(a.x * w, a.y * h); ctx.lineTo(p.x * w, p.y * h); ctx.stroke();
        if (!i) { ctx.beginPath(); ctx.arc(p.x * w, p.y * h, thickness / 2, 0, Math.PI * 2); ctx.fill(); }
      });
    }
  };
  const schedule = () => { cancelAnimationFrame(frame.current); frame.current = requestAnimationFrame(draw); };
  useLayoutEffect(() => { draw(); });
  useEffect(() => {
    const node = photo.current; if (!node) return;
    const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(schedule) : null;
    const loaded = () => { const img = node.querySelector('img'); if (img?.naturalWidth) { setRatio(img.naturalWidth / img.naturalHeight); setReady(true); } schedule(); };
    const failed = () => setReady(false);
    observer?.observe(node); node.addEventListener('load', loaded, true); node.addEventListener('error', failed, true); loaded(); window.addEventListener('resize', schedule);
    return () => { observer?.disconnect(); node.removeEventListener('load', loaded, true); node.removeEventListener('error', failed, true); window.removeEventListener('resize', schedule); cancelAnimationFrame(frame.current); };
  }, [id, visible]);
  const position = (event: { clientX: number; clientY: number; pressure: number; pointerType: string }) => {
    const rect = layer.current?.getBoundingClientRect(); if (!rect) return null;
    const p = normalizedPoint(event.clientX, event.clientY, rect);
    return p ? { ...p, pressure: event.pointerType === 'pen' && event.pressure > 0 ? event.pressure : null } : null;
  };
  function start(event: ReactPointerEvent<HTMLCanvasElement>) {
    if (!photo.current?.querySelector('img')?.naturalWidth) return;
    if (event.pointerType === 'touch' && penSeen.current && tool !== 'hand') return;
    if (event.pointerType === 'pen') { penSeen.current = true; pointers.current.clear(); }
    pointers.current.add(event.pointerId);
    if (pointers.current.size > 1) { gesture.current = null; schedule(); return; }
    if (event.button !== 0 || (event.pointerType === 'touch' && (penSeen.current || !touchPen) && tool !== 'hand')) return;
    const p = position(event); if (!p) return;
    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    if (tool === 'hand' || !editable) { gesture.current = { id: event.pointerId, x: event.clientX, y: event.clientY, before: current.current }; return; }
    if (!visible) setVisible(true);
    if (tool === 'eraser') {
      const rect = event.currentTarget.getBoundingClientRect();
      const strokes = current.current.strokes.filter(s => !hit(s, p, rect.width, rect.height));
      if (strokes.length !== current.current.strokes.length) change(edit(current.current, strokes));
    } else {
      if (state.strokes.length >= 2000) { setNotice('Достигнут лимит штрихов.'); return; }
      gesture.current = { id: event.pointerId, x: event.clientX, y: event.clientY, before: current.current,
        stroke: { id: strokeId(), tool: 'pen', color, width, points: [p] } }; schedule();
    }
  }
  function move(event: ReactPointerEvent<HTMLCanvasElement>) {
    const g = gesture.current; if (!g || g.id !== event.pointerId) return;
    if (!g.stroke) { change({ ...current.current, x: g.before.x + event.clientX - g.x, y: g.before.y + event.clientY - g.y }); return; }
    const coalesced = event.nativeEvent.getCoalescedEvents?.();
    const events = coalesced?.length ? coalesced : [event];
    for (const e of events) { const p = position(e); if (p && g.stroke.points.length < 4096) g.stroke.points.push(p); }
    if (g.stroke.points.length === 4096) setNotice('Достигнут лимит точек штриха. Начните следующий штрих.');
    schedule();
  }
  function finish(event: ReactPointerEvent<HTMLCanvasElement>, cancel = false) {
    pointers.current.delete(event.pointerId);
    const g = gesture.current; if (!g || g.id !== event.pointerId) return;
    gesture.current = null;
    if (g.stroke && !cancel && editable) change(edit(current.current, [...current.current.strokes, g.stroke]));
    schedule();
  }
  return <div className="annotation-photo">
    <div className="annotation-tools" role="group" aria-label="Инструменты фотографии">
      {editable && <>{(['pen', 'eraser', 'hand'] as const).map(t => <button className={`button button--secondary annotation-tool-${t}`} type="button" key={t} aria-pressed={tool === t} onClick={() => setTool(t)}>{t === 'pen' ? 'Перо' : t === 'eraser' ? 'Ластик' : 'Рука'}</button>)}
        <label className="annotation-tool-color">Цвет <input aria-label="Цвет пера" type="color" value={color} onChange={e => setColor(e.target.value)} /></label>
        <label className="annotation-tool-width">Толщина <select aria-label="Толщина пера" value={width} onChange={e => setWidth(Number(e.target.value))}><option value={.0015}>Тонкая</option><option value={.003}>Средняя</option><option value={.006}>Толстая</option></select></label>
        <button className="button button--secondary" type="button" disabled={!state.undo.length} onClick={() => change(undo(state))}>Отмена</button>
        <button className="button button--secondary annotation-tool-redo" type="button" disabled={!state.redo.length} onClick={() => change(redo(state))}>Повтор</button>
        <label className="annotation-tool-touch"><input type="checkbox" checked={touchPen} onChange={e => setTouchPen(e.target.checked)} />Рисовать пальцем</label></>}
      <button className="button button--secondary" type="button" aria-label="Уменьшить фото" disabled={state.zoom <= 1} onClick={() => change({ ...state, zoom: Math.max(1, state.zoom - .5) })}>−</button>
      <output aria-label="Масштаб">{Math.round(state.zoom * 100)}%</output>
      <button className="button button--secondary" type="button" aria-label="Увеличить фото" disabled={state.zoom >= 4} onClick={() => change({ ...state, zoom: Math.min(4, state.zoom + .5) })}>+</button>
      <button className="button button--secondary" type="button" onClick={() => change({ ...state, zoom: 1, x: 0, y: 0 })}>Вписать страницу</button>
      <label className="annotation-tool-visibility"><input type="checkbox" checked={visible} onChange={e => setVisible(e.target.checked)} />Показать пометки</label>
    </div>
    <p className="small muted annotation-status" role="status">Графические пометки: {state.strokes.length} штр. {notice}</p>
    <div className="annotation-viewport" tabIndex={0} aria-label="Просмотр фото" role="region" onKeyDown={e => {
      const delta = { ArrowLeft: [30, 0], ArrowRight: [-30, 0], ArrowUp: [0, 30], ArrowDown: [0, -30] }[e.key];
      if (delta && !e.altKey) { e.preventDefault(); change({ ...state, x: state.x + delta[0]!, y: state.y + delta[1]! }); }
    }}>
      <div ref={photo} className="annotation-image" style={{ width: `min(100%, calc(var(--annotation-height, 65vh) * ${ratio}))`, transform: `translate(${state.x}px, ${state.y}px) scale(${state.zoom})` }}>
        <AttachmentPreview api={api} id={id} page={page} annotation />
        <canvas ref={layer} hidden={!ready} aria-label={`Слой пометок страницы ${page}`} style={{ touchAction: editable && tool !== 'hand' ? 'none' : 'pinch-zoom' }}
          onPointerDown={start} onPointerMove={move} onPointerUp={e => finish(e)} onPointerCancel={e => finish(e, true)} onLostPointerCapture={e => finish(e, true)} />
      </div>
    </div>
    <p className="small muted">Оригинал доступен по ссылке под фотографией. Масштаб — кнопками; перемещение — рукой или стрелками. Для пера касания пальцем отключены по умолчанию. Жест масштабирования доступен в режиме «Рука».</p>
  </div>;
}
