import { useEffect, useId, useRef, useState } from 'react';
import { Button } from '../primitives';
import { ApiError } from '../../api/client';
import type { AttachmentApi, UploadedAttachment } from './api';
import './attachments.css';

type Page = { key: string; file: File; state: 'uploading' | 'uploaded' | 'failed'; progress: number;
  attachment?: UploadedAttachment; preview?: string; error?: string; busy?: boolean };
function errorText(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === 'AttachmentTooLarge') return 'Фото превышает допустимый размер.';
    if (error.code === 'UnsupportedAttachmentType') return 'Выберите фото в формате JPEG или PNG.';
    if (error.code === 'InvalidAttachment') return 'Не удалось прочитать изображение. Выберите другое фото.';
    if (error.status === 401) return 'Войдите снова, чтобы загрузить фото.';
    if (error.status === 403 || error.status === 404) return 'Нет доступа к фото.';
    if (error.code === 'AttachmentAlreadyAttached') return 'Фото уже отправлено и не может быть удалено.';
  }
  return 'Не удалось выполнить запрос. Попробуйте ещё раз.';
}

/** Pending resources only. The future solution screen owns Submit and local draft persistence. */
export function AttachmentUploader({ api, onChange }: { api: AttachmentApi;
  onChange?: (attachments: UploadedAttachment[], ready: boolean) => void }) {
  const inputId = useId();
  const [pages, setPages] = useState<Page[]>([]);
  const active = useRef(true);
  const controllers = useRef(new Set<AbortController>());
  const urls = useRef(new Set<string>());
  useEffect(() => {
    active.current = true;
    return () => { active.current = false; controllers.current.forEach(c => c.abort()); urls.current.forEach(URL.revokeObjectURL); };
  }, []);
  useEffect(() => { onChange?.(pages.flatMap(p => p.attachment ? [p.attachment] : []),
    pages.every(p => p.state === 'uploaded' && !p.busy)); }, [pages, onChange]);
  const update = (key: string, values: Partial<Page>) => {
    if (active.current) setPages(current => current.map(p => p.key === key ? { ...p, ...values } : p));
  };
  async function preview(key: string, attachment: UploadedAttachment, controller: AbortController) {
    try {
      const blob = await api.content(attachment.attachmentId, controller.signal);
      if (!active.current) return;
      const url = URL.createObjectURL(blob); urls.current.add(url); update(key, { preview: url, error: undefined });
    } catch (error) { if (!controller.signal.aborted) update(key, { error: `Предпросмотр: ${errorText(error)}` }); }
  }
  async function upload(page: Page) {
    const controller = new AbortController(); controllers.current.add(controller);
    update(page.key, { state: 'uploading', progress: 0, error: undefined });
    try {
      const attachment = await api.upload(page.file, controller.signal, progress => update(page.key, { progress }));
      update(page.key, { state: 'uploaded', attachment, progress: 100 });
      await preview(page.key, attachment, controller);
    } catch (error) { if (!controller.signal.aborted) update(page.key, { state: 'failed', error: errorText(error) }); }
    finally { controllers.current.delete(controller); }
  }
  function add(files: FileList | null) {
    const added = Array.from(files ?? []).map(file => ({ key: crypto.randomUUID(), file,
      state: 'uploading' as const, progress: 0 }));
    setPages(current => [...current, ...added]);
    added.forEach(page => { void upload(page); });
  }
  async function remove(page: Page) {
    const controller = new AbortController(); controllers.current.add(controller);
    update(page.key, { busy: true, error: undefined });
    try {
      if (page.attachment) await api.remove(page.attachment.attachmentId, controller.signal);
      if (!active.current) return;
      if (page.preview) { URL.revokeObjectURL(page.preview); urls.current.delete(page.preview); }
      setPages(current => current.filter(p => p.key !== page.key));
    } catch (error) { if (!controller.signal.aborted) update(page.key, { busy: false, error: errorText(error) }); }
    finally { controllers.current.delete(controller); }
  }
  function move(index: number, delta: number) {
    setPages(current => { const next = [...current]; [next[index], next[index + delta]] = [next[index + delta]!, next[index]!]; return next; });
  }
  async function retryPreview(page: Page) {
    const controller = new AbortController(); controllers.current.add(controller);
    try { await preview(page.key, page.attachment!, controller); } finally { controllers.current.delete(controller); }
  }
  return <section className="attachment-uploader" aria-label="Фотографии решения">
    <label htmlFor={inputId}>Добавить фото решения</label>
    <p id={`${inputId}-help`}>Выберите фотографии из галереи или сделайте снимок. JPEG или PNG, одна фотография на страницу.</p>
    <input id={inputId} type="file" accept="image/jpeg,image/png" multiple aria-describedby={`${inputId}-help`}
      onChange={event => { add(event.target.files); event.target.value = ''; }} />
    {pages.length === 0 && <p>Фотографии пока не добавлены.</p>}
    <ol className="attachment-pages">{pages.map((page, index) => <li key={page.key}>
      <h3>Страница {index + 1}</h3><p className="attachment-filename">{page.file.name}</p>
      {page.preview && <img src={page.preview} alt={`Страница ${index + 1} — фото решения`} />}
      <div role="status">{page.state === 'uploading' ? `Загрузка: ${page.progress}%` : page.state === 'uploaded' ? 'Фото загружено' : 'Ошибка загрузки'}</div>
      {page.state === 'uploading' && <progress value={page.progress} max={100} aria-label={`Загрузка страницы ${index + 1}`} />}
      {page.error && <p role="alert">{page.error}</p>}
      <div className="attachment-actions">
        {page.state === 'failed' && <Button variant="secondary" onClick={() => void upload(page)}>Повторить загрузку страницы {index + 1}</Button>}
        {page.attachment && !page.preview && page.error && <Button variant="secondary" onClick={() => void retryPreview(page)}>Повторить предпросмотр страницы {index + 1}</Button>}
        <Button variant="secondary" disabled={page.state === 'uploading' || page.busy} onClick={() => void remove(page)}>Удалить страницу {index + 1}</Button>
        <Button variant="secondary" disabled={index === 0} onClick={() => move(index, -1)} aria-label={`Страница ${index + 1}: переместить выше`}>Выше</Button>
        <Button variant="secondary" disabled={index === pages.length - 1} onClick={() => move(index, 1)} aria-label={`Страница ${index + 1}: переместить ниже`}>Ниже</Button>
      </div>
    </li>)}</ol>
  </section>;
}
