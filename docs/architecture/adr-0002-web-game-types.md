# ADR-0002: Which game types ship on web (Unity Web builds)

- **Status:** Proposed. Waiting for the owner's iPhone (Low Power Mode off) and low-end Android results; the gaps are marked **PENDING**.
- **Date:** 2026-11-11
- **Deciders:** Wisdom (owner), Claude Code (tech lead)
- **Evidence:** Prompt 0.7 spike (`/spike`, `make web-spike`, `docs/spikes/web-perf-checklist.md`)

## Context

GameVerse is Android first, web second (CLAUDE.md section 1), and the web is how iPhone users play until native iOS ships (section 8). CLAUDE.md section 3 says Unity games are embedded in the React app with `react-unity-webgl`, and that each game ships "its own small Web build, loaded only when played". Before building games, Prompt 0.7 measured what a Unity 6 Web build costs on real devices, so we know which game types the web can carry.

## What was measured

Two scenes, each a release Web build:

| Scene | Contents | Renderer |
|---|---|---|
| **2D** | 200 flipbook-animated sprites, one script each | URP 2D |
| **3D** | A car lapping an oval track, baked lightmaps, realtime car shadow | URP Forward+ |

Each was built in two project shapes:

| Shape | What's in the project |
|---|---|
| **Full** | The real project with everything installed today |
| **Lean** | URP plus the engine modules a game needs: audio, animation, particles, 2D/3D physics, web requests. No Localization, Addressables, Input System, UniTask, NativeWebSocket, Newtonsoft or shared DLLs |

All builds used the same settings:
- Brotli without decompression fallback, WebGL 2 only;
- IL2CPP optimised for size with disk-size LTO;
- High managed stripping, engine code stripping;
- exceptions "explicitly thrown only".

### Download size (Brotli, what a player downloads on first visit)

| Build | Total | wasm (code) | data (assets + shaders) |
|---|---|---|---|
| Full 2D | 9.96 MB | 6.20 MB | 3.68 MB |
| Full 3D | 10.42 MB | 6.19 MB | 4.14 MB |
| Lean 2D | 7.25 MB | 4.49 MB | 2.67 MB |
| Lean 3D | 7.75 MB | 4.50 MB | 3.17 MB |

What the build reports show:
- **Scene content is almost nothing.** The 2D sprite sheet is 16 KB.
- **The wasm is the floor.** About 4.5 MB is the Unity engine plus URP code. It is identical in every game and can't be shrunk by game work.
- **About 2.5 MB of the raw data is URP film-grain noise textures** (10 × 256 KB), included through URP's global settings even though no game uses film grain. Noise barely compresses, so this is likely about 2 MB of every download. Removing it is a follow-up (see Consequences).
- **The Unity splash logo is not a size lever.** The report listed it at 2.7 MB, but turning it off saved 0.06 MB.

### Load time and frame rate

Measured with the `/spike` page, which reports:

| Field | Meaning |
|---|---|
| download | When the build files finished arriving |
| startup | Download finished → playable, i.e. the device CPU |
| unity fps | Frames Unity rendered over 10 s after a 2 s warm-up |
| 1% low | Browser frame rate implied by the slowest 1% of frames |

| Device | Build | Cold → playable | Startup | Warm → playable | Unity fps | 1% low |
|---|---|---|---|---|---|---|
| Desktop Chrome, Intel UHD 620 | Full 2D / 3D | 2.31 / 1.84 s | 1.16 / 1.07 s | 1.15 / 1.21 s | 60 / 60 | 58.8 / 58.8 |
| Desktop Chrome, Intel UHD 620 | Lean 2D / 3D | 1.18 / 1.19 s | 0.59 / 0.62 s | 0.87 / 0.95 s | 60 / 60 | 58.8 / 58.8 |
| Desktop at 4x CPU throttle + Fast 4G (9 Mbps, 60 ms) | Full 2D / 3D | 14.96 / 16.52 s | 4.35 / 5.36 s | 4.78 / 4.44 s | 57.5 / 45.4 | 29.9 / 20 |
| Desktop at 4x CPU throttle + Fast 4G (9 Mbps, 60 ms) | Lean 2D / 3D | 13.22 / 13.21 s | 4.78 / 4.35 s | 4.02 / 4.09 s | 57.8 / 46.7 | 29.9 / 29.7 |
| Samsung Note 9 (Adreno 630), Chrome, over USB | Full 2D / 3D | 3.59 / 3.66 s | 2.08 / 2.57 s | 2.33 / 2.39 s | 59.9 / 60 | 59.5 / 59.5 |
| Samsung Note 9 (Adreno 630), Chrome, over USB | Lean 2D / 3D | 2.84 / 3.12 s | 1.85 / 2.12 s | 1.74 / 2.14 s | 59.9 / 60 | 59.5 / 59.5 |
| iPhone, iOS 26.5, Chrome (WebKit), Low Power Mode **on** | Full 2D / 3D | n/a (tunnel) | 0.42 / 0.52 s | – | capped at 30 | – |
| iPhone, Low Power Mode off | All four | **PENDING** | | | | |
| Low-end Android (owner's) | All four | **PENDING** | | | | |

Notes on these numbers:
- Unity fps and browser fps matched on every run (desktop and Note 9), so Unity kept pace with the display everywhere measured.
- On the Note 9, the lean builds start 0.2 to 0.5 s sooner (less code to compile), and frame rate is identical.
- iOS Low Power Mode caps every browser at 30 fps, so that run says nothing about the iPhone's frame rate. Its startup (about 0.5 s) is still the fastest of any device.
- Phone downloads over USB or the tunnel are not network-realistic. The **startup** column is the device's own cost.

### Delivery findings

1. **Brotli needs `Content-Encoding: br`.** Builds without the decompression fallback load only if the server sends that header for `*.br`, and browsers accept `br` only over HTTPS (or localhost).
   - `apps/web/vite-plugin-unity-brotli.ts` does this for dev and preview.
   - Traefik and nginx in production need the same rule.
2. **Cloudflare re-encodes our Brotli files.** Through a Cloudflare quick tunnel, the 6.2 MB wasm reached clients as gzip (8.7 MB), and sometimes nearly uncompressed. With `Cache-Control: no-transform` it arrived fully uncompressed (23.9 MB).
   - Production has Cloudflare in front (CLAUDE.md section 3).
   - Brotli pass-through must be configured and verified on the real zone before any game ships on web. Otherwise every download is 1.4x to 3x larger.
3. **Unity data caching works.** Warm loads transfer about 300 bytes and are playable in about 1 s on desktop and 1.7 to 2.1 s on the Note 9.
4. **URP Forward+ works on WebGL 2** with baked lightmaps and realtime shadows, with no errors on Chrome desktop, Android or iOS.

## Decision (proposed)

### 1. Game types on web

| Game type (games) | Web verdict | Why |
|---|---|---|
| Board and card, turn-based (Ludo, Whot, Draughts, Chess) | **Ship on web** | 60 fps everywhere measured. Turn-based play hides a slower first load. |
| 2D arcade and platformer (Twin Lanes, Wahala, Stickman Combat) | **Ship on web** | 2D scene held 57 to 60 fps even at 4x CPU throttle. |
| 2D physics (8-Ball Pool, Downhill if 2D) | **Ship on web, subject to the low-end Android result** | Same profile as 2D arcade; deterministic fixed-point physics is CPU-light. |
| 3D racer (Fabulous Super Race) | **Ship on web for mid-range and up, with a low-quality tier; PENDING low-end Android** | 60 fps on the Note 9, 45 fps at 4x throttle. Needs a quality toggle (lower resolution scale, no realtime shadow). |
| 2.5D fighter (Rival Realms) | **Decide when built; PENDING** | Needs a steady 60 fps for input timing. The 3D numbers suggest mid-range is fine and low-end is not. |

For every web game:
- **Canvas pixel ratio capped at 2** (the spike's setting). Phones report 3 to 4.
- **A first-visit download budget**, set when the low-end Android result is in. **PENDING:** proposed at 8 MB (Brotli) per game, including the engine.

### 2. Project hygiene that shrinks every web build (no scope change)

- **Remove default-template packages we never chose** from `apps/mobile`:
  - Unity IAP 4.15 (unsupported by Unity since June 2026);
  - Analytics, AI Navigation, Multiplayer Center, Timeline, uGUI, Collab;
  - the unused modules (cloth, terrain, vehicles, wind, video, XR, VR, umbra).
- **Keep the Unity splash screen and logo off** (done in `ProjectSetup`).

### 3. Open question for the owner: one shared web runtime, or one Web build per game

The spike shows each per-game build carries about 4.5 MB of engine plus URP code, and a browser can't share that between separate builds. Two shapes:

| | One Web build per game (CLAUDE.md today) | One shared web runtime + per-game Addressables content |
|---|---|---|
| First game | about 7 to 8 MB | about 7 to 8 MB |
| Each additional game | about 7 to 8 MB again | content only, typically under 1 to 2 MB |
| Cost | Simple; games fully isolated | One Unity Web player for the whole web catalogue; game content as Addressables (already the plan for Android); a game's code change ships a new runtime |

**Recommendation:**
- Ship **per-game builds for Ludo** (simplest; nothing else is on web yet).
- Revisit when the second web game ships (Whot), with a measured Addressables prototype.

This changes a locked line in CLAUDE.md section 3, so it needs your approval before any work.

## Consequences

- **Before the first web launch:**
  - Cloudflare Brotli pass-through, verified with real bytes on the production zone;
  - `Content-Encoding: br` on Traefik/nginx;
  - a CI check that fails if a game's Web build exceeds its budget.
- **Follow-up:** remove URP's film-grain textures from web builds (URP global settings, post-processing resources), then re-measure.
- **Follow-up:** the template package cleanup above, in its own commit with a full test run (EditMode, Android IL2CPP build, on-device self-check).
- **The spike is removed when this ADR is accepted:** `Assets/GameVerse/Spike`, `Editor/SpikeSetup.cs`, `BuildScript.BuildWebSpike`, the `/spike` route, `apps/web/scripts/spike-measure.mjs`, `infrastructure/scripts/web-spike-sizes.mjs`, `docs/spikes/`.
- **What stays:**
  - the Web player settings in `ProjectSetup`;
  - the Forward+ renderer;
  - `vite-plugin-unity-brotli.ts`, which is needed for every embedded game;
  - `react-unity-webgl`.
