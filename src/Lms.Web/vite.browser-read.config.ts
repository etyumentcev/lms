import { defineConfig } from 'vite';
// Separate public artifact: never contains a JWT or a public fixture/auth control route.
export default defineConfig({ build: { outDir: 'dist-browser-read', rollupOptions: { input: 'browser-read/index.html' } } });
