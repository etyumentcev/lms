const requests = new Set<AbortController>();
const objectUrls = new Set<string>();

export function beginSessionRequest(external?: AbortSignal) {
  const controller = new AbortController();
  const abort = () => controller.abort();
  if (external?.aborted) controller.abort();
  else external?.addEventListener('abort', abort, { once: true });
  requests.add(controller);
  return {
    signal: controller.signal,
    release() {
      requests.delete(controller);
      external?.removeEventListener('abort', abort);
    },
  };
}

export function createSessionObjectUrl(blob: Blob) {
  const url = URL.createObjectURL(blob);
  objectUrls.add(url);
  return url;
}

export function revokeSessionObjectUrl(url: string) {
  if (!objectUrls.delete(url)) return;
  URL.revokeObjectURL(url);
}

export function resetSessionResources() {
  for (const controller of requests) controller.abort();
  requests.clear();
  for (const url of objectUrls) URL.revokeObjectURL(url);
  objectUrls.clear();
}
