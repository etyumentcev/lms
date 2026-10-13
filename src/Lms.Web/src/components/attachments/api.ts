import type { ApiClient } from '../../api/client';

export type UploadedAttachment = {
  attachmentId: string; fileName: string; mediaType: string; size: number; createdAt: string; previewUrl: string;
};
export type AttachmentOperation = {
  uploadCommandId: string; uploadIntentId: string; deleteCommandId: string;
};
export type AttachmentApi = {
  upload(file: File, signal: AbortSignal, onUploadProgress: (percent: number) => void,
    operation?: AttachmentOperation): Promise<UploadedAttachment>;
  content(id: string, signal: AbortSignal): Promise<Blob>;
  remove(id: string, signal: AbortSignal, operation?: AttachmentOperation): Promise<void>;
};
export function decodeAttachment(value: unknown): UploadedAttachment {
  const x = value as UploadedAttachment;
  if (!x || !/^[\da-f-]{36}$/i.test(x.attachmentId) || typeof x.fileName !== 'string'
    || !['image/jpeg', 'image/png'].includes(x.mediaType) || !Number.isSafeInteger(x.size) || x.size <= 0
    || typeof x.createdAt !== 'string' || !Number.isFinite(Date.parse(x.createdAt))
    || x.previewUrl !== `/api/v1/attachments/${x.attachmentId}/content`) throw Error('Invalid attachment');
  return x;
}
export function attachmentApi(client: ApiClient): AttachmentApi {
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

async function sha256(file: File) {
  const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase();
}

/** Teacher-only adapter. Operation identities come from the owning draft and are never regenerated here. */
export function teacherAttachmentApi(client: ApiClient, workId: string): AttachmentApi {
  const work = encodeURIComponent(workId);
  return {
    async upload(file, signal, onUploadProgress, operation) {
      if (!operation) throw new Error('Teacher attachment operation identity is required');
      return client.request(`/problem-works/${work}/uploads/${encodeURIComponent(operation.uploadIntentId)}`,
        decodeAttachment, { method: 'POST', body: file, signal, onUploadProgress, csrf: true, headers: {
          'X-Command-Id': operation.uploadCommandId,
          'X-File-Name': encodeURIComponent(file.name),
          'X-Content-SHA256': await sha256(file),
        } });
    },
    content(id, signal) {
      return client.request(`/attachments/${encodeURIComponent(id)}/content`, value => {
        if (!(value instanceof Blob)) throw Error(); return value;
      }, { signal, responseType: 'blob' });
    },
    remove(id, signal, operation) {
      if (!operation) throw new Error('Teacher attachment operation identity is required');
      return client.request(`/problem-works/${work}/attachments/${encodeURIComponent(id)}`, () => undefined,
        { method: 'DELETE', signal, csrf: true, headers: { 'X-Command-Id': operation.deleteCommandId } });
    },
  };
}
