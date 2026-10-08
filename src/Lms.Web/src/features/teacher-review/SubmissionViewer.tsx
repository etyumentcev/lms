import { useState } from 'react';
import type { Submission } from '../../api/contracts/lessonWorkspace';
import type { AttachmentApi } from '../../components/attachments/api';
import { AttachmentPreview } from '../../components/attachments/AttachmentPreview';
import { MarkdownContent } from '../../components/MarkdownContent';
import { Button } from '../../components/primitives';
export function SubmissionViewer({ submission, api }: { submission: Submission; api: AttachmentApi }) {
  const [page, setPage] = useState(0);
  const [zoom, setZoom] = useState(100);
  const ids = submission.attachmentIds;
  function move(next: number) { setPage(Math.max(0, Math.min(ids.length - 1, next))); setZoom(100); }
  return <article aria-label={`Просмотр попытки №${submission.attemptNumber}`} className="review-submission">
    <h2>Попытка №{submission.attemptNumber}</h2>
    <p className="muted">Отправлена <time dateTime={submission.submittedAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(submission.submittedAt))}</time></p>
    {submission.bodyMarkdown && <div className="review-text"><MarkdownContent>{submission.bodyMarkdown}</MarkdownContent></div>}
    {ids.length > 0 ? <div className="review-viewer" role="group" aria-label="Фотографии решения">
      <nav className="review-toolbar" aria-label="Страницы решения">
        <Button variant="secondary" disabled={page === 0} onClick={() => move(page - 1)}>Предыдущая</Button>
        <span role="status">Страница {page + 1} из {ids.length}</span>
        <Button variant="secondary" disabled={page === ids.length - 1} onClick={() => move(page + 1)}>Следующая</Button>
      </nav>
      <div className="review-toolbar"><Button variant="secondary" disabled={zoom === 100} onClick={() => setZoom(value => value - 50)} aria-label="Уменьшить фото">−</Button>
        <output aria-label="Масштаб">{zoom}%</output>
        <Button variant="secondary" disabled={zoom === 300} onClick={() => setZoom(value => value + 50)} aria-label="Увеличить фото">+</Button>
        <Button variant="secondary" disabled={zoom === 100} onClick={() => setZoom(100)}>По ширине</Button>
      </div>
      <p className="small muted" id="review-viewer-help">В области фото: Alt + ← / → — сменить страницу. Увеличенное фото можно прокручивать; оригинал открывается по нажатию.</p>
      <div className="review-photo-scroll" tabIndex={0} role="region" aria-label="Просмотр фото" aria-describedby="review-viewer-help"
        onKeyDown={event => {
          if (event.altKey && ['ArrowLeft', 'ArrowRight'].includes(event.key)) {
            event.preventDefault(); move(page + (event.key === 'ArrowLeft' ? -1 : 1));
          }
        }}>
        <div className="review-photo" style={{ width: `${zoom}%` }}>
          <AttachmentPreview key={ids[page]} api={api} id={ids[page]!} page={page + 1} />
        </div>
      </div>
    </div> : <p className="muted">Фотографий в этой попытке нет.</p>}
  </article>;
}
