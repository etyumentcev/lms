import { loadEnv } from 'vite';
import { defineConfig } from 'vitest/config';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), 'LMS_');
  // Server-side setting, never embedded as a token or browser-side API origin.
  const proxy = env.LMS_API_TARGET ? { '/api': { target: env.LMS_API_TARGET, changeOrigin: true } } : undefined;
  return {
    build: { rollupOptions: { output: { manualChunks: { markdown: ['react-markdown', 'remark-math', 'rehype-katex'] } } } },
    server: { proxy }, preview: { proxy },
    test: { environment: 'jsdom', setupFiles: ['./src/test/setup.ts'], clearMocks: true },
  };
});
