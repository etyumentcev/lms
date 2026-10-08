import { useEffect, useState } from 'react';
import type { AttachmentApi } from './api';
import { Button, LoadingSkeleton } from '../primitives';

export function AttachmentImage({ url, page }: { url: string; page: number }) {
  return <a href={url} target="_blank" rel="noopener noreferrer" aria-label={`Открыть страницу ${page} в полном размере (новая вкладка)`}>
    <img src={url} alt={`Страница ${page} — фото решения`} />
  </a>;
}
/** Protected original, using the U1 content adapter and revocable browser object URLs. */
export function AttachmentPreview({ api, id, page }: { api: AttachmentApi; id: string; page: number }) {
  const [result, setResult] = useState<{ url?: string; failed?: boolean }>({});
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); let url: string | undefined;
    setResult({});
    void api.content(id, controller.signal).then(blob => {
      if (controller.signal.aborted) return;
      url = URL.createObjectURL(blob); setResult({ url });
    }).catch(() => { if (!controller.signal.aborted) setResult({ failed: true }); });
    return () => { controller.abort(); if (url) URL.revokeObjectURL(url); };
  }, [api, id, retry]);
  return result.url ? <AttachmentImage url={result.url} page={page} /> : result.failed
    ? <><p role="alert">Не удалось загрузить страницу {page}.</p><Button variant="secondary" onClick={() => setRetry(x => x + 1)}>Повторить предпросмотр страницы {page}</Button></>
    : <LoadingSkeleton label={`Загрузка страницы ${page}`} rows={2} />;
}
