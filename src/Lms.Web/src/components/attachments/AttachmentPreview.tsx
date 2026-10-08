import { useEffect, useState } from 'react';
import { ApiError } from '../../api/client';
import type { AttachmentApi } from './api';
import { Button, LoadingSkeleton } from '../primitives';

export function AttachmentImage({ url, page, onError }: { url: string; page: number; onError?: () => void }) {
  return <a href={url} target="_blank" rel="noopener noreferrer" aria-label={`Открыть страницу ${page} в полном размере (новая вкладка)`}>
    <img src={url} alt={`Страница ${page} — фото решения`} onError={onError} />
  </a>;
}
/** Protected original, using the U1 content adapter and revocable browser object URLs. */
export function AttachmentPreview({ api, id, page, annotation = false }: { api: AttachmentApi; id: string; page: number; annotation?: boolean }) {
  const [result, setResult] = useState<{ url?: string; failed?: string }>({});
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); let url: string | undefined;
    setResult({});
    void api.content(id, controller.signal).then(blob => {
      if (controller.signal.aborted) return;
      url = URL.createObjectURL(blob); setResult({ url });
    }).catch((error: unknown) => { if (!controller.signal.aborted) setResult({ failed: error instanceof ApiError && [401, 403].includes(error.status)
      ? 'Нет доступа к изображению.' : error instanceof ApiError && error.status === 404
        ? 'Изображение не найдено или недоступно.' : 'Не удалось загрузить изображение.' }); });
    return () => { controller.abort(); if (url) URL.revokeObjectURL(url); };
  }, [api, id, retry]);
  return result.url ? annotation ? <><img src={result.url} alt={`Страница ${page} — фото решения`} onError={() => setResult({ failed: 'Не удалось открыть изображение.' })} /><a className="annotation-original" href={result.url} target="_blank" rel="noopener noreferrer" aria-label={`Открыть страницу ${page} в полном размере (новая вкладка)`}>Открыть оригинал</a></> : <AttachmentImage url={result.url} page={page} onError={() => setResult({ failed: 'Не удалось открыть изображение.' })} /> : result.failed
    ? <><p role="alert">Не удалось загрузить страницу {page}. {result.failed}</p><Button variant="secondary" onClick={() => setRetry(x => x + 1)}>Повторить предпросмотр страницы {page}</Button></>
    : <LoadingSkeleton label={`Загрузка страницы ${page}`} rows={2} />;
}
