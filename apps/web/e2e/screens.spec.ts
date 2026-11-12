import { expect, test } from '@playwright/test';
import { catalogFixture } from '../src/test/catalogFixture';

// Smoke test and screenshots at 360x800 (phone) and 1440x900 (desktop), per CLAUDE.md section 9.
// Screenshots land in test-results/ for review; they are not pixel-compared (yet).

test.use({ reducedMotion: 'reduce' });

// No backend in e2e: the catalog API answers with the launch-library fixture (same data the Vitest suite uses).
test.beforeEach(async ({ page }) => {
  await page.route('**/catalog/games', (route) => route.fulfill({ json: catalogFixture }));
});

test('home renders every section without horizontal scroll', async ({ page }, info) => {
  await page.goto('/');
  await expect(page.getByRole('link', { name: 'Play Ludo' })).toBeVisible();
  for (const name of ['Continue playing', 'Daily quests', 'More games coming', 'Categories', 'Friends online']) {
    await expect(page.getByRole('heading', { name })).toBeVisible();
  }
  await page.evaluate(() => document.fonts.ready);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBe(0);
  await page.screenshot({ path: info.outputPath('home-fold.png') });
  await page.screenshot({ path: info.outputPath('home-full.png'), fullPage: true });
});

test('fonts are the self-hosted OFL families', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('link', { name: 'Play Ludo' })).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  const loaded = await page.evaluate(() => ({
    display: document.fonts.check('600 36px "Bricolage Grotesque"'),
    body: document.fonts.check('400 16px "Manrope"'),
  }));
  expect(loaded).toEqual({ display: true, body: true });
});

test('home loading skeleton', async ({ page }, info) => {
  // Hold the catalog response so the skeleton is on screen for the assertion (the stub otherwise answers instantly).
  let release: () => void = () => {};
  const held = new Promise<void>((resolve) => (release = resolve));
  await page.unroute('**/catalog/games');
  await page.route('**/catalog/games', async (route) => {
    await held;
    await route.fulfill({ json: catalogFixture });
  });
  await page.goto('/');
  await expect(page.getByLabel('Loading home')).toBeVisible();
  await page.screenshot({ path: info.outputPath('home-loading.png') });
  release();
  await expect(page.getByRole('link', { name: 'Play Ludo' })).toBeVisible();
});

test('component gallery', async ({ page }, info) => {
  await page.goto('/dev/components');
  await expect(page.getByRole('heading', { level: 1, name: 'Components' })).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: info.outputPath('components-full.png'), fullPage: true });
});

test('placeholder screen', async ({ page }, info) => {
  await page.goto('/play');
  await expect(page.getByRole('heading', { level: 1, name: 'Play' })).toBeVisible();
  await page.screenshot({ path: info.outputPath('play.png') });
});

test('error screen (ADR-0005)', async ({ page }, info) => {
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  await page.goto('/dev/crash');
  await expect(page.getByRole('heading', { level: 1, name: 'Something went wrong' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reload' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Back to Home' })).toBeVisible();
  // Without a DSN the error is not lost: reportError logs it to the console.
  expect(errors.some((e) => e.includes('GV_DEV_CRASH'))).toBe(true);
  await page.evaluate(() => document.fonts.ready);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBe(0);
  await page.screenshot({ path: info.outputPath('error-screen.png') });
});

test('games list: playable first, then coming soon', async ({ page }, info) => {
  await page.goto('/games');
  await expect(page.getByRole('heading', { level: 1, name: 'Games' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Ludo' }).first()).toBeVisible();
  await expect(page.getByLabel('Chess, coming soon')).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBe(0);
  await page.screenshot({ path: info.outputPath('games-full.png'), fullPage: true });
});
