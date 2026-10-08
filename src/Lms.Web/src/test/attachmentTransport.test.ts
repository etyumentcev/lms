import { afterEach, expect, it, vi } from 'vitest';
import { createApiClient } from '../api/client';

class UploadXhr {
  static last: UploadXhr;
  upload: { onprogress?: (event: { loaded: number; total: number; lengthComputable: boolean }) => void } = {};
  headers = new Map<string, string>();
  status = 201; responseText = '{"id":"ok"}'; responseURL = '';
  onload?: () => void; onerror?: () => void; onabort?: () => void;
  open = vi.fn(); send = vi.fn(); abort = vi.fn(() => this.onabort?.());
  constructor() { UploadXhr.last = this; }
  setRequestHeader(key: string, value: string) { this.headers.set(key.toLowerCase(), value); }
  getResponseHeader() { return 'application/json'; }
}
afterEach(() => vi.unstubAllGlobals());
async function start() {
  vi.stubGlobal('XMLHttpRequest', UploadXhr);
  const onUnauthenticated = vi.fn(), onUploadProgress = vi.fn(), controller = new AbortController();
  const client = createApiClient({ getAccessToken: async () => 'trusted-token', onUnauthenticated });
  const body = new FormData(); body.append('file', new Blob(['photo']), 'photo.png');
  const promise = client.request('/uploads', x => x, { method: 'POST', body, signal: controller.signal, onUploadProgress });
  await Promise.resolve();
  return { xhr: UploadXhr.last, promise, onUnauthenticated, onUploadProgress, controller, body };
}
it('progress adapter sends multipart with shared auth and actual progress', async () => {
  const { xhr, promise, onUploadProgress, body } = await start();
  expect(xhr.open).toHaveBeenCalledWith('POST', '/api/v1/uploads'); expect(xhr.headers.get('authorization')).toBe('Bearer trusted-token');
  expect(xhr.headers.has('content-type')).toBe(false); expect(xhr.send).toHaveBeenCalledWith(body);
  xhr.upload.onprogress?.({ loaded: 42, total: 100, lengthComputable: true }); expect(onUploadProgress).toHaveBeenCalledWith(42);
  xhr.onload?.(); await expect(promise).resolves.toEqual({ id: 'ok' });
});
it('progress adapter retains Problem Details and session callback', async () => {
  const { xhr, promise, onUnauthenticated } = await start(); xhr.status = 401;
  xhr.responseText = JSON.stringify({ type: 'urn:lms:error:Unauthenticated', title: 'Unauthenticated', code: 'Unauthenticated', status: 401, instance: '/uploads', traceId: 'test' });
  xhr.onload?.(); await expect(promise).rejects.toMatchObject({ status: 401, code: 'Unauthenticated' }); expect(onUnauthenticated).toHaveBeenCalledOnce();
});
it('progress adapter aborts underlying upload', async () => {
  const { xhr, promise, controller } = await start(); controller.abort(); await expect(promise).rejects.toMatchObject({ name: 'AbortError' }); expect(xhr.abort).toHaveBeenCalledOnce();
});
it('progress adapter uses the same network error contract', async () => {
  const { xhr, promise } = await start(); xhr.onerror?.(); await expect(promise).rejects.toMatchObject({ status: 0, code: 'NetworkError' });
});
