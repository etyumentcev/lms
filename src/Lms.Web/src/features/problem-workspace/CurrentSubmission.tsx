import type { Submission } from '../../api/contracts/lessonWorkspace';
import type { AttachmentApi } from '../../components/attachments/api';
import { AttachmentPreview } from '../../components/attachments/AttachmentPreview';
import { MarkdownContent } from '../../components/MarkdownContent';
export function CurrentSubmission({ submission, api }: { submission: Submission; api: AttachmentApi }) {
  return <section aria-labelledby="current-submission"><h2 id="current-submission">Отправленная попытка №{submission.attemptNumber}</h2>
    <p className="muted"><time dateTime={submission.submittedAt}>{new Intl.DateTimeFormat('ru', { dateStyle: 'long', timeStyle: 'short' }).format(new Date(submission.submittedAt))}</time></p>
    <ol className="attachment-pages">{submission.attachmentIds.map((id, index) => <li key={id}>
      <h3>Страница {index + 1}</h3><AttachmentPreview api={api} id={id} page={index + 1} />
    </li>)}</ol>
    {submission.bodyMarkdown && <MarkdownContent>{submission.bodyMarkdown}</MarkdownContent>}
  </section>;
}
