import { createApiClient } from './client';

/** Supplied by the trusted hosting application's session integration, never by URL/storage. */
export type AuthAdapter = {
  getAccessToken: () => string | null | Promise<string | null>;
  onUnauthenticated: () => void;
};
declare global { interface Window { lmsAuth?: AuthAdapter } }

export const browserApiClient = createApiClient({
  getAccessToken: () => window.lmsAuth?.getAccessToken() ?? null,
  onUnauthenticated: () => window.lmsAuth?.onUnauthenticated(),
  // Defer lookup until request time (also permits transport-level tests).
  fetch: (input, init) => globalThis.fetch(input, init),
});
