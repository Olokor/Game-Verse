#!/usr/bin/env node
// Prompt 0.7 web performance spike (throwaway, removed with ADR-0002).
// Loads /spike for each scene and prints what the page measured (window.__spikeResult): a cold load (empty cache) then a
// warm load (cached) per scene.
//
//   node scripts/spike-measure.mjs desktop [baseUrl]   Chrome on this PC with the real GPU: unthrottled, then 4x CPU
//                                                      throttle + ~Fast 4G network.
//   node scripts/spike-measure.mjs android <adb>       Chrome on the USB phone over adb (DevTools socket + adb reverse),
//                                                      Chrome must be open on the phone.
// Expects `vite preview` with VITE_SPIKE=true on port 4173.
import { execFileSync } from 'node:child_process';
import { chromium } from '@playwright/test';

const [mode = 'desktop', arg] = process.argv.slice(2);
const SCENES = ['2d', '3d'];
const TIMEOUT = 240_000;
const FAST_4G = { offline: false, latency: 60, downloadThroughput: (9 * 1024 * 1024) / 8, uploadThroughput: (1.5 * 1024 * 1024) / 8 };

const mb = (b) => (b / 1024 / 1024).toFixed(2);
const s = (ms) => (ms / 1000).toFixed(2);
const rows = [];

async function measure(page, base, scene, attempt = 1) {
  await page.goto(`${base}/spike?scene=${scene}`, { waitUntil: 'domcontentloaded' });
  try {
    await page.waitForFunction(() => window.__spikeResult?.fps, null, { timeout: TIMEOUT, polling: 1000 });
  } catch (error) {
    // The desktop run uses a real (visible) window; a stray click can interrupt a load. Retry once, then fail loudly.
    if (attempt > 1) throw error;
    console.log(`  ${scene}: no result within ${TIMEOUT / 1000}s, retrying once`);
    return measure(page, base, scene, attempt + 1);
  }
  return page.evaluate(() => window.__spikeResult);
}

function record(profile, load, r) {
  rows.push({ profile, load, ...r });
  console.log(
    `${profile.padEnd(22)} ${r.scene}  ${load.padEnd(4)}  build ${mb(r.download.bodyBytes)} MB  wire ${mb(r.download.transferBytes)} MB  ` +
      `downloaded ${s(r.download.finishedAt)} s  startup ${s(r.startup)} s  playable ${s(r.loadedAt)} s  ` +
      `fps ${r.fps.average} (1% low ${r.fps.low1}, hitches ${r.fps.longFrames})  ${r.canvas}@${r.devicePixelRatio}  gpu ${r.gpu}`,
  );
}

async function desktop(base) {
  // Headless Chrome falls back to software GL; run a real window on the real GPU so frame rates mean something.
  const browser = await chromium.launch({ headless: false, args: ['--ignore-gpu-blocklist'] });
  for (const [profile, throttled] of [['desktop', false], ['desktop 4xCPU+Fast4G', true]]) {
    for (const scene of SCENES) {
      const context = await browser.newContext({ viewport: { width: 1280, height: 900 } }); // fresh cache = cold load
      const page = await context.newPage();
      if (throttled) {
        const cdp = await context.newCDPSession(page);
        await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });
        await cdp.send('Network.enable');
        await cdp.send('Network.emulateNetworkConditions', FAST_4G);
      }
      record(profile, 'cold', await measure(page, base, scene));
      record(profile, 'warm', await measure(page, base, scene));
      await context.close();
    }
  }
  await browser.close();
}

async function android(adb) {
  if (!adb) throw new Error('usage: spike-measure.mjs android <adb path>');
  const run = (...a) => execFileSync(adb, a, { encoding: 'utf8' }).trim();
  run('reverse', 'tcp:4173', 'tcp:4173'); // phone's localhost:4173 -> this PC (localhost counts as a secure context)
  run('forward', 'tcp:9222', 'localabstract:chrome_devtools_remote');
  const model = run('shell', 'getprop', 'ro.product.model');
  const browser = await chromium.connectOverCDP('http://localhost:9222');
  const context = browser.contexts()[0];
  for (const scene of SCENES) {
    const page = await context.newPage();
    const cdp = await context.newCDPSession(page);
    await cdp.send('Network.enable');
    await cdp.send('Network.clearBrowserCache');
    await cdp.send('Storage.clearDataForOrigin', { origin: 'http://localhost:4173', storageTypes: 'all' });
    record(`android ${model}`, 'cold', await measure(page, 'http://localhost:4173', scene));
    record(`android ${model}`, 'warm', await measure(page, 'http://localhost:4173', scene));
    await page.close();
  }
  await browser.close();
}

if (mode === 'desktop') await desktop(arg ?? 'http://localhost:4173');
else if (mode === 'android') await android(arg);
else throw new Error(`unknown mode ${mode}`);

console.log('\nGV_SPIKE_ROWS ' + JSON.stringify(rows));
