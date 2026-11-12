#!/usr/bin/env node
// Installs the development APK on the USB-connected Android device, launches it, and checks the two self-check lines the
// Boot scene logs (apps/mobile/Assets/GameVerse/Platform/Diagnostics):
//   GV_SELFCHECK  shared-rules PRNG vectors under IL2CPP; must say "prng ok"
//   GV_ERRORCHECK error reporting (ADR-0005); "sent" or "disabled" (build made without UNITY_ERROR_DSN)
// When the build reports "sent" and GLITCHTIP_API_TOKEN is in .env, the event must also be found in GlitchTip's unity
// project. `adb reverse tcp:8000` lets the phone reach GlitchTip on this PC at 127.0.0.1:8000 (the DSN's host).
// Exits 0 only if every check passes. Usage: node device-check.mjs <adb> <apk>
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';

const [adb, apk] = process.argv.slice(2);
if (!adb || !apk) {
  console.error('usage: device-check.mjs <adb path> <apk path>');
  process.exit(2);
}
// stdio: ignore stdin and never hand our stdout to adb's background server (it would keep a parent pipe open).
const run = (...args) => execFileSync(adb, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const envValue = (key) => {
  if (!existsSync('.env')) return '';
  const line = readFileSync('.env', 'utf8').split(/\r?\n/).find((l) => l.startsWith(`${key}=`));
  return line ? line.slice(key.length + 1).trim() : '';
};

const devices = run('devices').split(/\r?\n/).slice(1).filter((l) => /\tdevice$/.test(l));
if (devices.length === 0) {
  console.error('No authorized device. Connect the phone over USB, enable USB debugging and accept the prompt.');
  process.exit(1);
}
console.log(`device: ${devices[0].split('\t')[0]}`);
console.log(run('install', '-r', apk).trim());
const glitchtipPort = envValue('GLITCHTIP_PORT') || '8000';
run('reverse', `tcp:${glitchtipPort}`, `tcp:${glitchtipPort}`);
run('logcat', '-c');
run('shell', 'monkey', '-p', 'app.gameverse.mobile', '-c', 'android.intent.category.LAUNCHER', '1');

const find = (log, prefix) => log.split(/\r?\n/).find((l) => l.includes(prefix));
let rules;
let errors;
for (const deadline = Date.now() + 45_000; (!rules || !errors) && Date.now() < deadline; ) {
  await sleep(1000);
  const log = run('logcat', '-d', '-s', 'Unity');
  rules ??= find(log, 'GV_SELFCHECK');
  errors ??= find(log, 'GV_ERRORCHECK');
}
console.log(rules ?? 'GV_SELFCHECK line not found within 45s');
console.log(errors ?? 'GV_ERRORCHECK line not found within 45s');

let ok = Boolean(rules?.includes(' prng ok ')) && Boolean(errors);
const token = envValue('GLITCHTIP_API_TOKEN');
if (ok && errors.includes(' sent ') && token) {
  const id = /id=([0-9a-f-]+)/i.exec(errors)?.[1]?.replace(/-/g, '');
  const url = `http://127.0.0.1:${glitchtipPort}/api/0/projects/gameverse/unity/issues/?query=GV_ERRORCHECK`;
  let found = false;
  for (const deadline = Date.now() + 60_000; !found && Date.now() < deadline; ) {
    const response = await fetch(url, { headers: { Authorization: `Bearer ${token}` } }).catch(() => undefined);
    const issues = response?.ok ? await response.json() : [];
    found = issues.some((issue) => String(issue.title).includes('GV_ERRORCHECK'));
    if (!found) await sleep(2000);
  }
  console.log(found ? `GlitchTip: GV_ERRORCHECK received (event ${id})` : 'GlitchTip: GV_ERRORCHECK not found within 60s');
  ok = found;
} else if (ok && errors.includes(' sent ')) {
  console.log('GlitchTip: not checked (no GLITCHTIP_API_TOKEN in .env)');
}
process.exit(ok ? 0 : 1);
