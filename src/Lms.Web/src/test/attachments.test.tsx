import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AttachmentUploader } from '../components/attachments/AttachmentUploader';
import { attachmentApi, decodeAttachment } from '../components/attachments/api';
import type { AttachmentApi, UploadedAttachment } from '../components/attachments/api';
import { ApiError, createApiClient } from '../api/client';

const attachment = (name = 'photo.png'): UploadedAttachment => {
  const id = crypto.randomUUID();
  return { attachmentId: id, fileName: name, mediaType: 'image/png', size: 70, createdAt: '2026-10-08T00:00:00Z', previewUrl: `/api/v1/attachments/${id}/content` };
};
const file = (name = 'photo.png') => new File(['photo'], name, { type: 'image/png' });
function setup(overrides: Partial<AttachmentApi> = {}) {
  const api: AttachmentApi = { upload: vi.fn(async (f: File) => attachment(f.name)), content: vi.fn(async () => new Blob(['photo'], { type: 'image/png' })), remove: vi.fn(async () => undefined), ...overrides };
  const onChange = vi.fn();
  const view = render(<AttachmentUploader api={api} onChange={onChange} />);
  return { api, onChange, ...view, input: screen.getByLabelText('Добавить фото решения') };
}
async function select(input: HTMLElement, ...files: File[]) { fireEvent.change(input, { target: { files } }); await screen.findByText('Фото загружено'); }
beforeEach(() => { URL.createObjectURL = vi.fn(() => 'blob:photo'); URL.revokeObjectURL = vi.fn(); });
afterEach(() => vi.restoreAllMocks());

describe('photo uploader foundation', () => {
  it('starts empty and ready without server requests', () => {
    const { api, onChange } = setup(); expect(screen.getByText('Фотографии пока не добавлены.')).toBeVisible();
    expect(api.upload).not.toHaveBeenCalled(); expect(onChange).toHaveBeenLastCalledWith([], true);
  });
  it('offers gallery/camera-compatible multiple image input with accessible help', () => {
    const { input } = setup(); expect(input).toHaveAttribute('type', 'file'); expect(input).toHaveAttribute('accept', 'image/jpeg,image/png');
    expect(input).toHaveAttribute('multiple'); expect(input).not.toHaveAttribute('capture'); expect(input).toHaveAccessibleDescription(/галереи/);
  });
  it('selects one image and returns only confirmed attachment IDs', async () => {
    const { input, api, onChange } = setup(); await select(input, file());
    expect(api.upload).toHaveBeenCalledTimes(1); expect(onChange.mock.lastCall?.[0][0].attachmentId).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Страница 1' })).toBeVisible();
  });
  it('retrieves protected content for a preview', async () => {
    const { input, api } = setup(); await select(input, file());
    expect(await screen.findByAltText('Страница 1 — фото решения')).toHaveAttribute('src', 'blob:photo'); expect(api.content).toHaveBeenCalledTimes(1);
  });
  it('keeps multiple pages in selection order despite reversed upload completion', async () => {
    let finish!: (a: UploadedAttachment) => void;
    const first = attachment('first.png'); const second = attachment('second.png');
    const upload = vi.fn().mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })).mockResolvedValueOnce(second);
    const { input, onChange } = setup({ upload }); fireEvent.change(input, { target: { files: [file('first.png'), file('second.png')] } });
    await screen.findByText('Фото загружено'); await act(async () => finish(first));
    expect(onChange.mock.lastCall?.[0].map((x: UploadedAttachment) => x.fileName)).toEqual(['first.png', 'second.png']);
    expect(screen.getAllByRole('heading').map(x => x.textContent)).toEqual(['Страница 1', 'Страница 2']);
  });
  it('reorders pages and returned attachment array', async () => {
    const { input, onChange } = setup(); fireEvent.change(input, { target: { files: [file('a.png'), file('b.png')] } });
    await waitFor(() => expect(screen.getAllByText('Фото загружено')).toHaveLength(2));
    await userEvent.click(screen.getByRole('button', { name: 'Страница 2: переместить выше' }));
    expect(onChange.mock.lastCall?.[0].map((x: UploadedAttachment) => x.fileName)).toEqual(['b.png', 'a.png']);
    expect(within(screen.getAllByRole('listitem')[0]!).getByText('b.png')).toBeVisible();
  });
  it('deletes pending server resource before removing page', async () => {
    const { input, api } = setup(); await select(input, file());
    await userEvent.click(screen.getByRole('button', { name: 'Удалить страницу 1' }));
    expect(api.remove).toHaveBeenCalledTimes(1); expect(screen.getByText('Фотографии пока не добавлены.')).toBeVisible();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:photo');
  });
  it('retains page if server deletion fails', async () => {
    const { input } = setup({ remove: vi.fn().mockRejectedValue(new ApiError(409, 'AttachmentAlreadyAttached')) }); await select(input, file());
    await userEvent.click(screen.getByRole('button', { name: 'Удалить страницу 1' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('уже отправлено'); expect(screen.getByRole('heading', { name: 'Страница 1' })).toBeVisible();
  });
  it('shows upload failure without reporting a confirmed attachment', async () => {
    const { input, onChange } = setup({ upload: vi.fn().mockRejectedValue(new ApiError(0, 'NetworkError')) });
    fireEvent.change(input, { target: { files: [file()] } }); expect(await screen.findByRole('alert')).toHaveTextContent('Попробуйте ещё раз');
    expect(onChange).toHaveBeenLastCalledWith([], false);
  });
  it('retries only the failed page and preserves successful pages', async () => {
    const upload = vi.fn().mockResolvedValueOnce(attachment('a.png')).mockRejectedValueOnce(new Error()).mockResolvedValueOnce(attachment('b.png'));
    const { input } = setup({ upload }); fireEvent.change(input, { target: { files: [file('a.png'), file('b.png')] } });
    await userEvent.click(await screen.findByRole('button', { name: 'Повторить загрузку страницы 2' }));
    await waitFor(() => expect(screen.getAllByText('Фото загружено')).toHaveLength(2));
    expect(upload.mock.calls.map(x => (x[0] as File).name)).toEqual(['a.png', 'b.png', 'b.png']);
  });
  it.each(['AttachmentTooLarge', 'UnsupportedAttachmentType', 'InvalidAttachment'])('displays server validation %s', async code => {
    const { input } = setup({ upload: vi.fn().mockRejectedValue(new ApiError(400, code)) });
    fireEvent.change(input, { target: { files: [file()] } }); expect(await screen.findByRole('alert')).toBeVisible();
  });
  it('displays real progress and does not treat 100 percent transport as success', async () => {
    const { input, onChange } = setup({ upload: vi.fn((_f, _s, progress) => { progress(100); return new Promise<UploadedAttachment>(() => {}); }) });
    fireEvent.change(input, { target: { files: [file()] } });
    expect(await screen.findByRole('progressbar')).toHaveAttribute('value', '100'); expect(screen.queryByText('Фото загружено')).not.toBeInTheDocument();
    expect(onChange).toHaveBeenLastCalledWith([], false); expect(screen.getByRole('button', { name: 'Удалить страницу 1' })).toBeDisabled();
  });
  it('supports keyboard page reordering', async () => {
    const { input, onChange } = setup(); fireEvent.change(input, { target: { files: [file('a.png'), file('b.png')] } });
    await waitFor(() => expect(screen.getAllByText('Фото загружено')).toHaveLength(2));
    screen.getByRole('button', { name: 'Страница 1: переместить ниже' }).focus(); await userEvent.keyboard('{Enter}');
    expect(onChange.mock.lastCall?.[0].map((x: UploadedAttachment) => x.fileName)).toEqual(['b.png', 'a.png']);
  });
  it('can remove a failed page without issuing server delete', async () => {
    const { input, api } = setup({ upload: vi.fn().mockRejectedValue(new Error()) }); fireEvent.change(input, { target: { files: [file()] } });
    await screen.findByRole('alert'); await userEvent.click(screen.getByRole('button', { name: 'Удалить страницу 1' })); expect(api.remove).not.toHaveBeenCalled();
  });
  it('retries preview without uploading duplicate resources', async () => {
    const content = vi.fn().mockRejectedValueOnce(new Error()).mockResolvedValueOnce(new Blob(['photo']));
    const { input, api } = setup({ content }); await select(input, file());
    await userEvent.click(await screen.findByRole('button', { name: 'Повторить предпросмотр страницы 1' }));
    expect(await screen.findByAltText('Страница 1 — фото решения')).toBeVisible(); expect(api.upload).toHaveBeenCalledTimes(1);
  });
  it('aborts active requests when unmounted', async () => {
    const upload = vi.fn(() => new Promise<UploadedAttachment>(() => {})); const { input, unmount } = setup({ upload });
    fireEvent.change(input, { target: { files: [file()] } }); unmount(); expect((upload.mock.calls as unknown as [File, AbortSignal][])[0]![1].aborted).toBe(true);
  });
});

it('attachment adapter uses only upload/content/delete routes, same auth and Problem Details', async () => {
  const item = attachment(); const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(item))).mockResolvedValueOnce(new Response('photo')).mockResolvedValueOnce(new Response(null, { status: 204 }));
  const client = createApiClient({ getAccessToken: () => 'token', fetch }); const api = attachmentApi(client);
  // No progress callback => fetch path permits transport-level inspection without XHR mocking.
  const form = new FormData(); form.append('file', file());
  await client.request('/uploads', decodeAttachment, { method: 'POST', body: form });
  await api.content(item.attachmentId, new AbortController().signal); await api.remove(item.attachmentId, new AbortController().signal);
  expect(fetch.mock.calls.map(x => x[0])).toEqual(['/api/v1/uploads', `/api/v1/attachments/${item.attachmentId}/content`, `/api/v1/attachments/${item.attachmentId}`]);
  expect(fetch.mock.calls[0]![1].headers.get('Authorization')).toBe('Bearer token'); expect(fetch.mock.calls[0]![1].headers.has('Content-Type')).toBe(false);
  expect(fetch.mock.calls[0]![1].body).toBe(form);
});
