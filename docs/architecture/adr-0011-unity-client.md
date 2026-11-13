# ADR-0011: The Unity client (Ludo, platform screens, networking)

- **Status:** Accepted (filled in part by part during Prompt 1.6)
- **Date:** 2026-11-13
- **Deciders:** Claude Code (tech lead), Wisdom (owner: approved the Prompt 1.6 plan)

## Context

Prompt 1.6 builds the first playable Unity client:
- the Ludo game module on the shared engine;
- the platform screens (lobby, matchmaking, private room, results);
- the networking that connects them to the backend of Prompts 1.1 to 1.5.

## Decisions: networking (part A)

### API location
- A `Resources` asset (`AppConfig`) holds the base URL.
- The committed asset stays empty. `BuildScript` writes `GAMEVERSE_API_URL` into it for the duration of a build, then clears it, the same pattern as the Sentry DSN (ADR-0005).
- Empty means `http://127.0.0.1:8080`: the editor talks to `make up` directly, and a USB phone reaches it through `adb reverse tcp:8080 tcp:8080`.
- **Development builds only** may use plain HTTP (`InsecureHttpOption.DevelopmentOnly`). Release builds refuse it, so production traffic is always TLS.

### HTTP
- `ApiClient` sends JSON with `UnityWebRequest` (the same on Android, iOS and Web) and `GameVerseJson`.
- Errors arrive as problem+json and become `ApiException`, with field errors and a single sentence fit to show a player. A missing answer is status 0 ("can't reach GameVerse").
- A 401 renews the access token once and retries.

### Sign-in and secrets
- **First run:** one-tap guest (DESIGN_SYSTEM screen 1).
- **The device keeps** the guest id, its device secret and the rotating refresh token. The access token stays in memory only.
- **Android:** the secrets are encrypted with AES-256-GCM using a key generated inside the Android Keystore. Only base64(iv + ciphertext) is written to PlayerPrefs.
  - If the key is lost (data cleared, restore to another phone), the stored values can't be read and the player becomes a new guest. Upgrading the guest to an email account (ADR-0006) is how a player keeps progress across devices.
- **Editor and desktop:** plain PlayerPrefs, development only.
- **Renewing the session:** the refresh token first, then the device secret. Renewals are serialised, so parallel 401s refresh once.

### Realtime
- **`RealtimeConnection` is main-thread only.**
  - The socket (NativeWebSocket on devices) puts its events on a queue, and `Tick()` drains it every frame.
  - No game or UI code ever runs on a socket thread.
- **Liveness:**
  - it answers `ping`;
  - it treats 45 seconds of silence as a dead link (the server pings every 15);
  - it reconnects with backoff (0.5, 1, 2, 4, 8 seconds), getting a fresh ticket each time.
- **Ordering and resends:**
  - it drops stale `stateVersion`s, but keeps a `snapshot` of the current version;
  - unacknowledged actions are resent after a reconnect with their original seq. The server applies a seq at most once (ADR-0009), so a resend is safe.
- **Seqs start from the current Unix time in milliseconds.** The server remembers the highest seq per seat for the whole match, so an app restarted mid-game must never count from 1 again.
- **A ticket request that gets 404** (the session is gone, or the player isn't seated) stops the reconnecting.
- **`GameHost`** implements `IGameHost` over the connection.
  - The game's `action_submitted` is sent as an `action`. The wire seq is the connection's, not the game's.
  - A refused action triggers a resync. The game re-enables its input on any new state, so it needs no error channel.
  - Turn clocks, presence and the result go to the platform UI, never into the game.

## Decisions: the Ludo module (part B)

### Presentation only
- **`GameVerse.Games.Ludo` contains no rules.**
  - `LudoStateMapper` rebuilds an engine `LudoState` from the seat's view (no PRNG), so `LudoEngine.LegalActions` decides which tokens light up.
  - The server checks every action again.
  - `LudoTransition` works out what to animate (a roll, one token's path, captures) by comparing consecutive states. A jump of more than one step (a reconnect snapshot) is shown without animation.
- **`LudoController`** owns input:
  - only seats played on this device can act;
  - one action in flight at a time;
  - any new state, even a rejection's snapshot, re-enables input.

### The board is drawn in code
- **`BoardElement` draws with UI Toolkit's `Painter2D`:**
  - the classic 15×15 board with four yards and the 52-square track, which cuts the four inner corners diagonally as real boards do;
  - start squares, home columns, stars (only when safe squares are on) and the centre triangles.
- **It's original art** with no asset pack (DESIGN_SYSTEM section 8), and crisp at any resolution.
- **Tokens:**
  - each carries its colour **and** a shape (red circle, green square, yellow triangle, blue diamond), so colour is never the only signal;
  - a volt ring marks the tokens that can move;
  - tokens sharing a square fan out so each stays tappable.
- **Colours come from new tokens** (`color.game.ludo.*`), and motion timings from `motion.game.*`:

  | Motion | Timing |
  |---|---|
  | Dice tumble | 600ms |
  | Hop per square, with a small arc and a squash | 90ms |
  | Capture arc back to the yard | 420ms |

### Motion: built-in, not LitMotion
- **The plan named LitMotion.** Every animated thing is a UI Toolkit element, so a 40-line tween helper (`GameVerse.UI.Motion.Tween`) is enough.
  - It is driven per frame, with the design system's cubic-bezier and an Android "remove animations" check (reduced motion jumps to the end).
  - This avoids a package dependency.
  - LitMotion can still come in for world-space games (CLAUDE.md allows it).

### Sound and haptics
- **Effects are synthesised when the game loads** (`AudioClip.Create`): dice rattle, hop tick, capture whoosh, home chime, win fanfare.
  - Original, a few kilobytes, nothing to license.
  - A recorded sound set can replace them without code changes outside `LudoSounds`.
- **Haptics** use Android one-shot vibration effects (a light tap on press). They're toggled with `gv.haptics`; sound with `gv.sound`.

### Pass-and-play
- **`LocalLudoHost`** runs the engine on the device, seeded from the secure random generator. It feeds the module exactly like the server does, through `ApplyServerState` with versioned states.
- **No account, no network, no rewards.**
- **A full-screen "Pass to Blue" curtain** waits for a tap between human players.
