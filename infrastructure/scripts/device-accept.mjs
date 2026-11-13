// Prompt 1.6 device acceptance (ADR-0011): the phone plays a full private-room Ludo game through the real backend.
//
//   make up                 # the backend on this PC
//   make android            # the development APK
//   make device-accept      # this script: node device-accept.mjs <adb> <apk> [api]
//
// 1. Installs the APK and forwards the phone's 127.0.0.1:8080 to this PC (adb reverse).
// 2. A bot (this script) signs in as a guest, opens a 2-player room and readies up.
// 3. Launches the app with `-e gv_autoplay_room CODE` (development builds only): it signs in, opens Home, the Ludo
//    lobby, joins the room, readies up and plays by itself through the normal screens and input path.
// 4. The bot starts the game and plays its seat over /ws/play until the server ends the match.
// Passes when the match ends on the server and the phone logs GV_ACCEPT for that session.
import { execFileSync } from 'node:child_process';

const [adb, apk, api = 'http://127.0.0.1:8080'] = process.argv.slice(2);
if (!adb || !apk) {
  console.error('usage: device-accept.mjs <adb path> <apk path> [api base url]');
  process.exit(2);
}

const sh = (...args) => execFileSync(adb, args, { encoding: 'utf8' });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(method, path, token, body) {
  const res = await fetch(api + path, {
    method,
    headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

async function until(check, ms, what) {
  const end = Date.now() + ms;
  for (;;) {
    const value = await check();
    if (value) return value;
    if (Date.now() > end) throw new Error(`timed out waiting for ${what}`);
    await sleep(500);
  }
}

// --- the phone ---
const devices = sh('devices').split(/\r?\n/).slice(1).filter((l) => /\tdevice$/.test(l));
if (devices.length === 0) {
  console.error('No authorized device. Connect the phone over USB, enable USB debugging and accept the prompt.');
  process.exit(1);
}
console.log(`device: ${devices[0].split('\t')[0]}`);
console.log(sh('install', '-r', apk).trim());
sh('reverse', 'tcp:8080', 'tcp:8080');
const activity = sh('shell', 'cmd', 'package', 'resolve-activity', '--brief', 'app.gameverse.mobile').trim().split(/\r?\n/).pop();

// --- the bot opens a room ---
const bot = await call('POST', '/auth/guest');
const room = await call('POST', '/rooms', bot.accessToken, { gameId: 'ludo', playerCount: 2 });
await call('POST', `/rooms/${room.code}/ready`, bot.accessToken, { ready: true });
console.log(`room ${room.code} opened by the bot`);

sh('logcat', '-c');
sh('shell', 'am', 'force-stop', 'app.gameverse.mobile');
sh('shell', 'am', 'start', '-n', activity, '-e', 'gv_autoplay_room', room.code);
console.log(`launched ${activity} with gv_autoplay_room=${room.code}`);

// --- the phone joins and readies; the bot starts ---
const ready = await until(async () => {
  const r = await call('GET', `/rooms/${room.code}`, bot.accessToken);
  return r.seats.every((s) => s.playerId && s.ready) ? r : null;
}, 120000, 'the phone to join and ready up');
console.log(`phone joined as ${ready.seats[1].displayName}`);
const started = await call('POST', `/rooms/${room.code}/start`, bot.accessToken);
const sessionId = started.sessionId;
console.log(`game started: session ${sessionId}`);

// --- the bot plays its seat ---
const manifest = await call('GET', `/sessions/${sessionId}/manifest`, bot.accessToken);
const seat = manifest.players.find((p) => p.id === bot.userId).seat;
const ended = await new Promise((resolve, reject) => {
  const ws = new WebSocket(`${manifest.realtime.wsUrl}?ticket=${encodeURIComponent(manifest.realtime.ticket)}`);
  let state = null;
  let turnSeat = null;
  let pending = null;
  let token = 0;
  let seq = Date.now();
  const act = () => {
    if (pending !== null || !state || turnSeat !== seat) return;
    const payload = state.phase === 'roll' ? { roll: true } : state.phase === 'move' ? { move: token } : null;
    if (!payload) return;
    pending = ++seq;
    ws.send(JSON.stringify({ t: 'action', seq: pending, sessionId, payload }));
  };
  ws.onerror = () => reject(new Error('bot socket error'));
  ws.onmessage = (event) => {
    const env = JSON.parse(event.data);
    if (env.t === 'ping') ws.send(JSON.stringify({ t: 'pong', seq: 0, sessionId }));
    if (env.t === 'state' || env.t === 'snapshot') {
      state = env.payload.state;
      turnSeat = env.payload.turnSeat;
      if (pending !== null && (env.payload.appliedSeq === pending || env.t === 'snapshot')) {
        pending = null;
        token = 0;
      }
    }
    if (env.t === 'error' && env.payload.seq === pending) {
      token = env.payload.code === 'illegal_move' ? token + 1 : token;
      pending = null;
    }
    if (env.t === 'ended') {
      ws.close();
      resolve(env.payload);
      return;
    }
    setTimeout(act, 300); // a person's pace: well inside the server's message budget
  };
  setTimeout(() => reject(new Error('the game did not end within 15 minutes')), 15 * 60000);
});
console.log(`match ended on the server: ${JSON.stringify(ended)}`);

// --- the phone saw it too ---
const line = await until(async () => sh('logcat', '-d', '-s', 'Unity').split(/\r?\n/).find((l) => l.includes(`GV_ACCEPT session=${sessionId}`)), 30000, 'GV_ACCEPT in logcat');
console.log(line.trim());
const fair = await call('GET', `/sessions/${sessionId}/fairness`, bot.accessToken);
console.log(`fairness revealed=${fair.revealed}`);
console.log('DEVICE ACCEPTANCE: PASS');
