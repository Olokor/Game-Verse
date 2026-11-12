import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { execSync } from 'node:child_process';
import { unityBrotli } from './vite-plugin-unity-brotli';

/** Short commit of this build, the release tag on reported errors (ADR-0005). */
function gitRelease(): string {
  try {
    return execSync('git rev-parse --short HEAD', { encoding: 'utf8' }).trim();
  } catch {
    return '';
  }
}

export default defineConfig({
  plugins: [react(), tailwindcss(), unityBrotli()],
  define: { __GV_RELEASE__: JSON.stringify(gitRelease()) },
  // One .env at the repo root for every app. Vite only exposes VITE_-prefixed values to the browser bundle.
  envDir: '../..',
  server: { port: 5173, strictPort: true },
  preview: { port: 4173, strictPort: true },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    css: false,
  },
});
