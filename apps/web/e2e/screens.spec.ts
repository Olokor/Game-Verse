import { expect, test } from '@playwright/test';

// Smoke test and screenshots at 360x800 (phone) and 1440x900 (desktop), per CLAUDE.md section 9.
// Screenshots land in test-results/ for review; they are not pixel-compared (yet).

test.use({ reducedMotion: 'reduce' });

test('home renders every section without horizontal scroll', async ({ page }, info) => {
  await page.goto('/');
  await expect(page.getByRole('link', { name: 'Play Ludo' })).toBeVisible();
  for (const name of ['Continue playing', 'Daily quests', 'Categories', 'Friends online']) {
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
  await page.goto('/');
  await expect(page.getByLabel('Loading home')).toBeVisible();
  await page.screenshot({ path: info.outputPath('home-loading.png') });
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
