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

## Decisions: the platform screens (part C)

### Shell
- **`Boot` builds the shell in code** on the scene's UI document:
  - `AppServices` holds the session, the router, live connections, the overlay and toasts;
  - the screens are plain classes, each with a UXML layout in `Platform/Resources/Screens`.
- **The router is a stack:**
  - the top screen gets `Tick()`;
  - Android back closes an open sheet first, then asks the screen (games ask to confirm leaving);
  - opening or closing a screen closes any sheet.
- **Transitions:** a 240ms slide-and-fade (motion.base). Under reduced motion it's a fade only (motion.fast), and sheets do the same.
- **The Boot scene is regenerated only when it is missing or wrong.** Before, every `ProjectSetup` run rewrote it and churned its object ids in git.
  - The camera now carries an `AudioListener`, without which no game sound would play.

### Screens

| Screen | What it does |
|---|---|
| Welcome | One-tap guest sign-in; a returning player is signed in behind a skeleton |
| Home | The real catalog, as on web |
| Lobby | Catalog modes as tiles; the player counts each mode allows; AI level; best of 1 or 3; the house-rules sheet; join by code; my rating |
| Matchmaking | Polls once a second |
| Private room | The live lobby socket, seats with shapes and ready states, the host's rules and Start, with the reason when it can't start yet |
| Game | Chips with a turn-clock bar, a reconnect banner, Leave (resign) after a confirm |
| Results | Placements, the reason, the new rating after ranked games, back to the room (rematch) or the lobby |

### Accessibility and states (from the ui-reviewer pass)
- **Touch targets:**
  - tokens keep a 44px touch area even when the disc drawn is smaller;
  - choice pills, the code field and the header buttons use `layout.touch-target`.
- **Never colour alone:**
  - "on" choices are filled, "off" are outlined;
  - avatars show the seat's token shape;
  - AI seats have no online dot.
- **States:**
  - Home has empty, loading and error states;
  - every button that calls the server shows its loading state;
  - leave and cancel can't fire twice;
  - the die has a pressed state.
- **New tokens:** `opacity.disabled`, `opacity.muted`, `opacity.inactive`, `motion.toast-hold` and `color.bg.scrim`.

### Not done yet (from the review)
- a celebration animation on Results (Rive, later);
- a QR code on the room code;
- fully tokenised pulse sizes and letter spacing;
- icons on the Back and Leave buttons.

### Checking the screens
- `make unity-screens` (with `make up` running) renders every screen at 360×800 into `apps/mobile/TestResults/screens/`.
- It runs as an opt-in PlayMode test that drives the real screens against the backend.

## Decisions: the premium table (owner request, 2026-11-13)

**Brief:** the owner found the first Ludo table "not premium" and asked for polish while keeping the brand colours. Two review passes (the ui-reviewer agent) shaped the result.

### Board
- **A raised plate** with a soft layered shadow and a lit rim.
- **Yards:** dark wells with a rim in the seat colour and recessed token slots.
- **Track:** tiles with a lit top edge and a shadowed lip.
- **Start squares** carry a direction chevron.
- **Home columns** brighten toward the centre.
- **Stars** are soft.
- **The centre medallion** is ringed in brand volt, with a volt core.

### Turn light
- The acting seat's yard glows and the others dim.
- **The glow is a soft texture,** generated at runtime and used as a tinted 9-slice background (`Glow.cs`).
  - Painter2D has no blur, and stacked strokes banded into stripes.
  - Breathing changes only `style.opacity`, so it costs no geometry: this matters at 60fps on mid-range phones.

### Pieces
- **Tokens:**
  - a contact shadow, rim, lit body, specular glint and the shape emblem;
  - larger while resting in their yard;
  - when they can move, they lift, and one crisp volt ring breathes by opacity;
  - a press state;
  - "×n" when a seat stacks tokens on one square.
- **The die:** a bevelled cube in a dock at the bottom (the thumb zone), with one crisp volt ring when it's your roll (no halo), a rise-and-squash tumble, a volt flash on a six, and a "TAP TO ROLL" hint.

### Dock
The prompt is shown with the acting seat's colour and shape, and the dock is tinted toward that colour.

### Moments (`Effects.cs`)

| Moment | Effect |
|---|---|
| Capture | a burst |
| Home | a ripple from the centre |
| Win | a winner card, and confetti with a paper flip |

- **Particles are pooled** and capped at 30 (tested).
- **Under reduced motion,** a six shows the ring for a beat, the win card fades in, and no particles spawn.

### HUD
- Compact chips in one row.
- **The ring around the avatar is the only turn signal.**
- The status shows only what matters: You, AI, Away, places, Left.

### Pass-and-play curtain
The seat's emblem, "Pass to Green", and a single "I'm Green" button.

### Constraints kept
- **No new hues:** light and shade are mixes of the seat colours toward `text.primary` or `bg.base`.
- **New tokens:** `color.bg.shadow-soft`, `color.bg.shadow-strong`, `motion.game.capture-burst`, `motion.game.home-ripple`.
- **Frame pacing:** development builds log `GV_FPS` (the average and the worst frame) every 10 seconds, to check the 60fps budget on devices.
