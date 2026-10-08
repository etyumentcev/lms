import type { ApiClient } from '../../api/client';

export type UploadedAttachment = {
  attachmentId: string; fileName: string; mediaType: string; size: number; createdAt: string; previewUrl: string;
};
export function decodeAttachment(value: unknown): UploadedAttachment {
  const x = value as UploadedAttachment;
  if (!x || !/^[\da-f-]{36}$/i.test(x.attachmentId) || typeof x.fileName !== 'string'
    || !['image/jpeg', 'image/png'].includes(x.mediaType) || !Number.isSafeInteger(x.size) || x.size <= 0
    || typeof x.createdAt !== 'string' || !Number.isFinite(Date.parse(x.createdAt))
    || x.previewUrl !== `/api/v1/attachments/${x.attachmentId}/content`) throw Error('Invalid attachment');
  return x;
}
export function attachmentApi(client: ApiClient) {
  return {
    upload(file: File, signal: AbortSignal, onUploadProgress: (percent: number) => void) {
      const body = new FormData(); body.append('file', file);
      return client.request('/uploads', decodeAttachment, { method: 'POST', body, signal, onUploadProgress });
    },
    content(id: string, signal: AbortSignal) {
      return client.request(`/attachments/${encodeURIComponent(id)}/content`, value => {
        if (!(value instanceof Blob)) throw Error(); return value;
      }, { signal, responseType: 'blob' });
    },
    remove(id: string, signal: AbortSignal) {
      return client.request(`/attachments/${encodeURIComponent(id)}`, () => undefined, { method: 'DELETE', signal });
    },
  };
}
export type AttachmentApi = ReturnType<typeof attachmentApi>;
