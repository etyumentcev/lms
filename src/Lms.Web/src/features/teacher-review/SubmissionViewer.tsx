import { useEffect, useRef, useState } from 'react';
import type { Submission } from '../../api/contracts/lessonWorkspace';
import type { AttachmentApi } from '../../components/attachments/api';
import { AttachmentPreview } from '../../components/attachments/AttachmentPreview';
import { AnnotationPhoto } from '../../components/annotations/AnnotationPhoto';
import { newPage, type AnnotatedAttachment, type PageState } from '../../components/annotations/model';
import { MarkdownContent } from '../../components/MarkdownContent';
import { Button } from '../../components/primitives';
export function SubmissionViewer({ submission, api, annotations = [], editable = false, pages: external, changePages }: {
  submission: Submission; api: AttachmentApi; annotations?: AnnotatedAttachment[]; editable?: boolean;
  pages?: Record<string, PageState>; changePages?: (pages: Record<string, PageState>) => void;
}) {
  const [page, setPage] = useState(0), [full, setFull] = useState(false);
  const [local, setLocal] = useState<Record<string, PageState>>({});
  const root = useRef<HTMLDivElement>(null), fullButton = useRef<HTMLButtonElement>(null);
  const ids = submission.attachmentIds, id = ids[page];
  const pages = external ?? local;
  const state = id ? pages[id] ?? newPage(annotations.find(a => a.attachmentId === id)?.strokes) : newPage();
  function move(next: number) { const focused = root.current?.querySelector('[aria-label="Просмотр фото"]') === document.activeElement; setPage(Math.max(0, Math.min(ids.length - 1, next))); if (focused) requestAnimationFrame(() => root.current?.querySelector<HTMLElement>('[aria-label="Просмотр фото"]')?.focus()); }
  async function fullscreen() {
    if (full) { if (document.fullscreenElement) await document.exitFullscreen(); setFull(false); fullButton.current?.focus(); }
    else { setFull(true); try { await root.current?.requestFullscreen?.(); } catch { /* CSS fallback remains active. */ } }
  }
  useEffect(() => {
    const changed = () => { if (!document.fullscreenElement) setFull(false); };
    const key = (e: KeyboardEvent) => { if (e.key === 'Escape') { setFull(false); fullButton.current?.focus(); } };
    document.addEventListener('fullscreenchange', changed); document.addEventListener('keydown', key);
    return () => { document.removeEventListener('fullscreenchange', changed); document.removeEventListener('keydown', key); };
  }, []);
  useEffect(() => {
    if (!full) return;
    const previous = document.body.style.overflow; document.body.style.overflow = 'hidden';
    // The fallback traps keyboard focus within the photo workspace as well as hiding surrounding UI.
    const trap = (e: KeyboardEvent) => {
      if (e.key !== 'Tab') return;
      const nodes = root.current?.querySelectorAll<HTMLElement>('button:not(:disabled),input,select,a,[tabindex="0"]');
      if (!nodes?.length) return;
      const first = nodes[0]!, last = nodes[nodes.length - 1]!;
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    };
    fullButton.current?.focus(); document.addEventListener('keydown', trap);
    return () => { document.body.style.overflow = previous; document.removeEventListener('keydown', trap); };
  }, [full]);
  return <article aria-label={`Просмотр попытки №${submission.attemptNumber}`} className="review-submission">
    <h2>Попытка №{submission.attemptNumber}</h2>
    <p className="muted">Отправлена <time dateTime={submission.submittedAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(submission.submittedAt))}</time></p>
    {submission.bodyMarkdown && <div className="review-text"><MarkdownContent>{submission.bodyMarkdown}</MarkdownContent></div>}
    {id ? <div ref={root} className={`review-viewer ${full ? 'annotation-fullscreen' : ''}`} role={full ? 'dialog' : 'group'} aria-modal={full || undefined} aria-label="Фотографии решения"
      onKeyDown={e => { if (e.altKey && ['ArrowLeft', 'ArrowRight'].includes(e.key)) { e.preventDefault(); move(page + (e.key === 'ArrowLeft' ? -1 : 1)); } }}>
      <nav className="review-toolbar" aria-label="Страницы решения">
        <Button variant="secondary" disabled={page === 0} onClick={() => move(page - 1)}>Предыдущая</Button>
        <span role="status">Страница {page + 1} из {ids.length}</span>
        <Button variant="secondary" disabled={page === ids.length - 1} onClick={() => move(page + 1)}>Следующая</Button>
        <button className="button button--secondary" type="button" ref={fullButton} onClick={() => void fullscreen()}>{full ? 'Выйти из полного экрана' : 'На весь экран'}</button>
      </nav>
      <div className="review-viewer-body">
        <ol className="review-thumbnails" aria-label="Миниатюры страниц">{ids.map((attachmentId, index) => <li key={attachmentId}>
          <AttachmentPreview api={api} id={attachmentId} page={index + 1} thumbnail selected={index === page} onSelect={() => move(index)} />
        </li>)}</ol>
        <AnnotationPhoto key={id} api={api} id={id} page={page + 1} state={state} editable={editable}
          change={next => (changePages ?? setLocal)({ ...pages, [id]: next })} />
      </div>
    </div> : <p className="muted">Фотографий в этой попытке нет.</p>}
  </article>;
}
