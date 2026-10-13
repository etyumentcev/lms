import { beginSessionRequest } from './sessionResources';

/** C4 Problem Details transport, not domain entities or feature read-model DTOs. */
export type ApiProblemDetails = {
  type: string;
  title: string;
  status: number;
  instance: string;
  code: string;
  traceId: string;
};

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    public readonly problem?: ApiProblemDetails,
  ) {
    super(code);
    this.name = 'ApiError';
  }
}

function parseProblem(value: unknown): ApiProblemDetails | undefined {
  if (typeof value !== 'object' || value === null) return;
  const data = value as Record<string, unknown>;
  if (typeof data.status !== 'number' || !Number.isInteger(data.status)) return;
  if (!['type', 'title', 'instance', 'code', 'traceId'].every(key => typeof data[key] === 'string')) return;
  if (!data.code) return;
  return data as ApiProblemDetails;
}

export type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  headers?: HeadersInit;
  csrf?: boolean;
  signal?: AbortSignal;
  responseType?: 'blob';
  onUploadProgress?: (percent: number) => void;
};
export type ApiClientOptions = {
  basePath?: string;
  getAccessToken: () => string | null | Promise<string | null>;
  onUnauthenticated?: () => void;
  fetch?: typeof globalThis.fetch;
  sessionScoped?: boolean;
};

/** No token storage, fake identity, automatic retry, refresh or feature query/cache policy. */
export function createApiClient({ basePath = '/api/v1', getAccessToken, onUnauthenticated, sessionScoped = true,
  fetch: fetchRequest = globalThis.fetch }: ApiClientOptions) {
  if (!/^\/(?!\/)[\w/-]+$/.test(basePath)) throw new Error('Use a same-origin API base path');
  const base = basePath.replace(/\/+$/, '');
  let csrf: { bearer: string | null; token: string } | null = null;
  async function csrfToken(bearer: string | null, signal?: AbortSignal) {
    if (csrf?.bearer === bearer) return csrf.token;
    const headers = new Headers({ Accept: 'application/json' });
    if (bearer) headers.set('Authorization', `Bearer ${bearer}`);
    let response: Response;
    try {
      response = await fetchRequest(`${base}/auth/csrf`, { headers, signal, credentials: 'same-origin', redirect: 'error', cache: 'no-store' });
    } catch (error) {
      if (signal?.aborted || (error instanceof Error && error.name === 'AbortError')) throw error;
      throw new ApiError(0, 'NetworkError');
    }
    if (response.status === 401) onUnauthenticated?.();
    let value: unknown;
    try { value = await response.json(); } catch { throw new ApiError(response.status, response.ok ? 'InvalidResponse' : 'HttpError'); }
    if (!response.ok) {
      const problem = parseProblem(value); throw new ApiError(response.status, problem?.code ?? 'HttpError', problem);
    }
    const token = (value as { token?: unknown } | null)?.token;
    if (typeof token !== 'string' || !token) throw new ApiError(response.status, 'InvalidResponse');
    csrf = { bearer, token }; return token;
  }
  return {
    async request<T>(path: string, decode: (value: unknown) => T, options: RequestOptions = {}): Promise<T> {
      const scoped = sessionScoped ? beginSessionRequest(options.signal) : null;
      const signal = scoped?.signal ?? options.signal;
      try {
        // Only relative API paths; do not forward bearer tokens to arbitrary URLs or redirects.
        const url = new URL(`${base}${path}`, 'https://lms.invalid');
        if (!path.startsWith('/') || path.startsWith('//') || /[\\#]/.test(path)
          || !url.pathname.startsWith(`${base}/`)) throw new Error('Invalid relative API path');
        const token = await getAccessToken();
        signal?.throwIfAborted();
        const headers = new Headers(options.headers);
        headers.set('Accept', 'application/json, application/problem+json');
        if (token) headers.set('Authorization', `Bearer ${token}`);
        if (options.csrf) headers.set('X-CSRF-TOKEN', await csrfToken(token, signal));
        const multipart = options.body instanceof FormData;
        const binary = options.body instanceof Blob ? options.body : null;
        if (binary && !headers.has('Content-Type')) headers.set('Content-Type', binary.type || 'application/octet-stream');
        if (options.body !== undefined && !multipart && !binary) headers.set('Content-Type', 'application/json');
        const body = multipart || binary ? options.body as XMLHttpRequestBodyInit
          : options.body === undefined ? undefined : JSON.stringify(options.body);
        let response: Response;
        try {
          response = options.onUploadProgress && body !== undefined
            ? await uploadTransport(`${url.pathname}${url.search}`, headers, body, { ...options, signal })
            : await fetchRequest(`${url.pathname}${url.search}`, {
            method: options.method ?? 'GET', headers, body, signal,
            credentials: 'same-origin', redirect: 'error', cache: 'no-store',
          });
        } catch (error) {
          if (signal?.aborted || (error instanceof Error && error.name === 'AbortError')) throw error;
          throw new ApiError(0, 'NetworkError');
        }
        if (response.status === 401) onUnauthenticated?.();
        let data: unknown;
        try { data = response.status === 204 ? undefined : response.ok && options.responseType === 'blob' ? await response.blob() : await response.json(); }
        catch {
          if (signal?.aborted) signal.throwIfAborted();
          throw new ApiError(response.status, response.ok ? 'InvalidResponse' : 'HttpError');
        }
        if (!response.ok) {
          const problem = parseProblem(data);
          throw new ApiError(response.status, problem?.code ?? 'HttpError', problem);
        }
        try { return decode(data); }
        catch { throw new ApiError(response.status, 'InvalidResponse'); }
      } finally { scoped?.release(); }
    },
  };
}

export type ApiClient = ReturnType<typeof createApiClient>;

/** Progress adapter for multipart and protected raw uploads; decoding remains in ApiClient. */
function uploadTransport(url: string, headers: Headers, body: XMLHttpRequestBodyInit, options: RequestOptions): Promise<Response> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    const cleanup = () => options.signal?.removeEventListener('abort', abort);
    const abort = () => { xhr.abort(); cleanup(); reject(new DOMException('Aborted', 'AbortError')); };
    xhr.open(options.method ?? 'POST', url);
    xhr.withCredentials = true;
    headers.forEach((value, key) => xhr.setRequestHeader(key, value));
    xhr.upload.onprogress = event => {
      if (event.lengthComputable) options.onUploadProgress?.(Math.round(event.loaded / event.total * 100));
    };
    xhr.onload = () => {
      cleanup();
      if (xhr.responseURL && new URL(xhr.responseURL).origin !== window.location.origin) {
        reject(new Error('Unexpected upload redirect')); return;
      }
      resolve(new Response(xhr.status === 204 ? null : xhr.responseText, { status: xhr.status,
        headers: { 'Content-Type': xhr.getResponseHeader('Content-Type') ?? '' } }));
    };
    xhr.onerror = () => { cleanup(); reject(new Error('Upload network failure')); };
    xhr.onabort = () => { cleanup(); reject(new DOMException('Aborted', 'AbortError')); };
    options.signal?.addEventListener('abort', abort, { once: true });
    if (options.signal?.aborted) { abort(); return; }
    xhr.send(body);
  });
}
