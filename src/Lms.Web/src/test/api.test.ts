import { describe, expect, it, vi } from 'vitest';
import { ApiError, createApiClient } from '../api/client';

function problem(status: number, code: string) {
  return { type: `urn:lms:error:${code}`, title: 'Human title is not the code', status,
    instance: '/api/v1/resource', code, traceId: 'trace-1' };
}
function jsonResponse(data: unknown, status = 200) { return new Response(JSON.stringify(data), { status }); }
const identity = (data: unknown) => data;

describe('API foundation', () => {
  it('reads a fresh token per request, forwards cancellation and decodes unknown success data', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValueOnce(jsonResponse({ value: 7 }))
      .mockResolvedValueOnce(jsonResponse({ value: 8 }));
    const token = vi.fn().mockResolvedValueOnce('first').mockResolvedValueOnce('second');
    const client = createApiClient({ getAccessToken: token, fetch });
    const signal = new AbortController().signal;
    const decode = (value: unknown) => {
      if (!value || typeof value !== 'object' || !('value' in value) || typeof value.value !== 'number') throw Error();
      return value.value;
    };
    expect(await client.request('/resource', decode, { method: 'POST', body: { input: 1 }, signal })).toBe(7);
    expect(await client.request('/resource', decode)).toBe(8);
    const [url, options] = fetch.mock.calls[0]!;
    expect(url).toBe('/api/v1/resource');
    expect(new Headers(options?.headers).get('Authorization')).toBe('Bearer first');
    expect(new Headers(fetch.mock.calls[1]![1]?.headers).get('Authorization')).toBe('Bearer second');
    expect(options).toMatchObject({ body: '{"input":1}', method: 'POST', signal, redirect: 'error' });
  });
  it.each([[401, 'Unauthenticated'], [403, 'Forbidden'], [409, 'ConcurrencyConflict'], [422, 'FeedbackRequired']])(
    'preserves %s Problem Details code without retry', async (status, code) => {
      const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse(problem(status as number, code as string), status as number));
      const unauthenticated = vi.fn();
      const client = createApiClient({ getAccessToken: () => null, fetch, onUnauthenticated: unauthenticated });
      await expect(client.request('/resource', identity)).rejects.toMatchObject({ status, code, problem: { traceId: 'trace-1' } });
      expect(fetch).toHaveBeenCalledOnce();
      expect(unauthenticated).toHaveBeenCalledTimes(status === 401 ? 1 : 0);
      expect(new Headers(fetch.mock.calls[0]![1]?.headers).has('Authorization')).toBe(false);
    },
  );
  it.each([null, {}, { code: 'Forbidden' }, { ...problem(500, 'InternalError'), code: '' }, '<html>bad gateway</html>'])(
    'handles malformed error envelopes without interpreting human text (%j)', async data => {
      const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(new Response(typeof data === 'string' ? data : JSON.stringify(data), { status: 502 }));
      const client = createApiClient({ getAccessToken: () => null, fetch });
      await expect(client.request('/resource', identity)).rejects.toMatchObject({ status: 502, code: 'HttpError' });
    },
  );
  it('normalizes network failures', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockRejectedValue(new TypeError('offline'));
    await expect(createApiClient({ getAccessToken: () => null, fetch }).request('/resource', identity))
      .rejects.toEqual(new ApiError(0, 'NetworkError'));
    expect(fetch).toHaveBeenCalledOnce();
  });
  it('preserves cancellation and never sends an already aborted request', async () => {
    const controller = new AbortController(); controller.abort();
    const fetch = vi.fn<typeof globalThis.fetch>();
    await expect(createApiClient({ getAccessToken: () => null, fetch }).request('/resource', identity, { signal: controller.signal }))
      .rejects.toMatchObject({ name: 'AbortError' });
    expect(fetch).not.toHaveBeenCalled();
  });
  it('rejects invalid success shapes at the decoder boundary', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(jsonResponse({ surprise: true }));
    await expect(createApiClient({ getAccessToken: () => null, fetch }).request('/resource', () => { throw Error(); }))
      .rejects.toMatchObject({ status: 200, code: 'InvalidResponse' });
  });
  it('supports an explicitly decoded empty 204 result', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>().mockResolvedValue(new Response(null, { status: 204 }));
    await expect(createApiClient({ getAccessToken: () => null, fetch }).request('/resource', value => {
      if (value !== undefined) throw Error();
    })).resolves.toBeUndefined();
  });
  it.each(['https://other.test', '//other.test', '/../outside', '/%2e%2e/outside', '/x\\y', '/x#fragment'])(
    'rejects path %s before reading a token or making a request', async path => {
      const fetch = vi.fn<typeof globalThis.fetch>(); const token = vi.fn();
      await expect(createApiClient({ getAccessToken: token, fetch }).request(path, identity)).rejects.toThrow();
      expect(fetch).not.toHaveBeenCalled(); expect(token).not.toHaveBeenCalled();
    },
  );
});
