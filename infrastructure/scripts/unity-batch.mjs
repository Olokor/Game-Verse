#!/usr/bin/env node
// Runs a Unity batch-mode command and returns reliably.
//
// Why: Unity 6000.6.8f1 on Windows sometimes hangs during shutdown ("Killing ADB server ...") after an Android build
// or setup has finished and called EditorApplication.Exit. Our editor scripts log a completion marker (GV_SETUP,
// GV_BUILD); once it appears, Unity gets GRACE_MS to exit by itself, then the process tree (editor + import workers) is
// ended. The exit code comes from the marker, never from the kill. If no marker appears, Unity's own exit code is used.
//
// Usage: node unity-batch.mjs <log file> <marker prefix> <success text> -- <unity exe> <args...>
//   e.g. node unity-batch.mjs apps/mobile/Logs/android.log "GV_BUILD android" "result=Succeeded" -- Unity.exe -batchmode ...
import { spawn, execFileSync } from 'node:child_process';
import { existsSync, readFileSync, rmSync } from 'node:fs';

const GRACE_MS = 60_000;
const POLL_MS = 2_000;

const sep = process.argv.indexOf('--');
const [logFile, marker, success] = process.argv.slice(2, sep);
const [exe, ...args] = sep > 0 ? process.argv.slice(sep + 1) : [];
if (!logFile || !marker || !success || !exe) {
  console.error('usage: unity-batch.mjs <log file> <marker prefix> <success text> -- <unity exe> <args...>');
  process.exit(2);
}

rmSync(logFile, { force: true }); // never read a marker left by a previous run
const unity = spawn(exe, [...args, '-logFile', logFile], { stdio: 'inherit' });

let markerLine;
let markerSeenAt;
let killed = false;

const findMarker = () => {
  if (!existsSync(logFile)) return undefined;
  return readFileSync(logFile, 'utf8').split(/\r?\n/).find((l) => l.startsWith(marker));
};

const killTree = (pid) => {
  if (process.platform === 'win32') {
    try {
      execFileSync('taskkill', ['/T', '/F', '/PID', String(pid)], { stdio: 'ignore' });
    } catch {
      // already gone
    }
  } else {
    try {
      process.kill(pid, 'SIGKILL');
    } catch {
      // already gone
    }
  }
};

const timer = setInterval(() => {
  if (!markerLine) {
    markerLine = findMarker();
    if (markerLine) {
      markerSeenAt = Date.now();
      console.log(markerLine);
    }
  } else if (Date.now() - markerSeenAt > GRACE_MS && !killed) {
    killed = true;
    console.log(`Unity did not exit ${GRACE_MS / 1000}s after "${marker}"; ending the editor process tree (known shutdown hang).`);
    killTree(unity.pid);
  }
}, POLL_MS);

unity.on('exit', (code) => {
  clearInterval(timer);
  markerLine ??= findMarker();
  if (markerLine) {
    const ok = markerLine.includes(success);
    console.log(`${marker}: ${ok ? 'ok' : 'FAILED'}${killed ? ' (editor ended after grace period)' : ''}`);
    process.exit(ok ? 0 : 1);
  }
  console.error(`No "${marker}" line in ${logFile}; Unity exit code ${code}. See the log.`);
  process.exit(code === 0 ? 1 : code ?? 1);
});
