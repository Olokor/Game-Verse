import { defineConfig, devices } from '@playwright/test';

// Smoke + screenshots against a production build (vite preview). Run: pnpm --filter @gameverse/web e2e
export default defineConfig({
  testDir: './e2e',
  outputDir: './test-results',
  reporter: 'list',
  use: { baseURL: 'http://localhost:4173', colorScheme: 'dark' },
  projects: [
    { name: 'phone-360', use: { ...devices['Desktop Chrome'], viewport: { width: 360, height: 800 }, hasTouch: true, isMobile: true } },
    { name: 'desktop-1440', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } },
  ],
  webServer: {
    command: 'pnpm build && pnpm preview',
    url: 'http://localhost:4173',
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: { VITE_DEV_ROUTES: 'true' },
  },
});
