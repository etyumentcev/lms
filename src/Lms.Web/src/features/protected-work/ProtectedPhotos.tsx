import { useEffect, useState } from 'react';
import { ApiError, type ApiClient } from '../../api/client';
import { createSessionObjectUrl, revokeSessionObjectUrl } from '../../api/sessionResources';

type Photo = { id: string; href: string };
export function ProtectedPhotos({ path, client }: { path: string; client: ApiClient }) {
  const [photos, setPhotos] = useState<Photo[]>([]);
  const [page, setPage] = useState(0);
  const [image, setImage] = useState<{ href: string; url: string }>();
  const url = image?.href === photos[page]?.href ? image?.url : undefined;
  const [error, setError] = useState<string>();
  const [zoom, setZoom] = useState(100);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setPhotos([]); setPage(0); setError(undefined);
    void client.request(path.slice('/api/v1'.length), value => {
      const items = (value as { items?: Photo[] })?.items;
      if (!Array.isArray(items) || items.length > 10 || items.some(p => !/^[0-9a-f-]{36}$/i.test(p.id) || p.href !== `${path}/${p.id}`)) throw new Error('Invalid photos');
      return items;
    }, { signal: controller.signal }).then(items => { if (!controller.signal.aborted) setPhotos(items); })
      .catch((e: unknown) => { if (!controller.signal.aborted) setError(message(e)); });
    return () => controller.abort();
  }, [path, client, reload]);
  useEffect(() => {
    const controller = new AbortController(); let objectUrl: string | undefined;
    setImage(undefined); setZoom(100);
    const photo = photos[page];
    if (photo) {
      setError(undefined);
      void client.request(photo.href.slice('/api/v1'.length), value => {
        if (!(value instanceof Blob) || !['image/png', 'image/jpeg'].includes(value.type) || value.size > 10 * 1024 * 1024) throw new Error('Invalid photo');
        return value;
      }, { signal: controller.signal, responseType: 'blob' }).then(blob => {
        if (!controller.signal.aborted) { objectUrl = createSessionObjectUrl(blob); setImage({ href: photo.href, url: objectUrl }); }
      }).catch((e: unknown) => { if (!controller.signal.aborted) setError(message(e)); });
    }
    return () => { controller.abort(); if (objectUrl) revokeSessionObjectUrl(objectUrl); };
  }, [photos, page, client]);
  return <section aria-label="Страницы решения">
    {error ? <p role="alert">{error}</p> : !url && <p role="status">Загрузка фотографии…</p>}
    <div className="photo-controls">
      {photos.map((p, i) => <button type="button" key={p.id} aria-pressed={page === i} onClick={() => setPage(i)}>Страница {i + 1}</button>)}
      {url && <label>Масштаб <select value={zoom} onChange={e => setZoom(Number(e.target.value))}>
        {[50, 100, 150, 200].map(n => <option key={n} value={n}>{n}%</option>)}</select></label>}
      <button type="button" onClick={() => { setImage(undefined); setPhotos([]); setReload(n => n + 1); }}>Обновить фотографии</button>
    </div>
    {url && <div className="photo-viewport"><img src={url} alt={`Страница ${page + 1} решения`} style={{ width: `${zoom}%`, maxWidth: 'none' }}
      onError={() => { setImage(undefined); setError('Фотография недоступна или повреждена.'); }} /></div>}
  </section>;
}
function message(e: unknown) {
  return e instanceof ApiError && e.status === 401 ? 'Войдите, чтобы открыть фотографию.' : e instanceof ApiError && e.status === 403 ? 'Нет доступа к фотографии.' : 'Фотография недоступна или повреждена.';
}
