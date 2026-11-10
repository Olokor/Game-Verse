#!/usr/bin/env node
// Installs the development APK on the USB-connected Android device, launches it, and prints the shared-rules
// self-check line the Boot scene logs (GV_SELFCHECK, see apps/mobile/Assets/GameVerse/Platform/Diagnostics).
// Exits 0 only if the device reports "prng ok". Usage: node device-check.mjs <adb> <apk>
import { execFileSync } from 'node:child_process';

const [adb, apk] = process.argv.slice(2);
if (!adb || !apk) {
  console.error('usage: device-check.mjs <adb path> <apk path>');
  process.exit(2);
}
const run = (...args) => execFileSync(adb, args, { encoding: 'utf8' });

const devices = run('devices').split(/\r?\n/).slice(1).filter((l) => /\tdevice$/.test(l));
if (devices.length === 0) {
  console.error('No authorized device. Connect the phone over USB, enable USB debugging and accept the prompt.');
  process.exit(1);
}
console.log(`device: ${devices[0].split('\t')[0]}`);
console.log(run('install', '-r', apk).trim());
run('logcat', '-c');
run('shell', 'monkey', '-p', 'app.gameverse.mobile', '-c', 'android.intent.category.LAUNCHER', '1');

const deadline = Date.now() + 30_000;
let line;
while (!line && Date.now() < deadline) {
  execFileSync(process.execPath, ['-e', 'setTimeout(()=>{},1000)']);
  line = run('logcat', '-d', '-s', 'Unity').split(/\r?\n/).find((l) => l.includes('GV_SELFCHECK'));
}
console.log(line ?? 'GV_SELFCHECK line not found within 30s');
process.exit(line && line.includes(' prng ok ') ? 0 : 1);
