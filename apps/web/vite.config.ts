import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { unityBrotli } from './vite-plugin-unity-brotli';

// Prompt 0.7 spike: phones reach `vite preview` through a Cloudflare quick tunnel (docs/spikes/web-perf-checklist.md).
const spike = process.env.VITE_SPIKE === 'true';

export default defineConfig({
  plugins: [react(), tailwindcss(), unityBrotli()],
  server: { port: 5173, strictPort: true },
  preview: { port: 4173, strictPort: true, ...(spike ? { allowedHosts: ['.trycloudflare.com'] } : {}) },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
});
