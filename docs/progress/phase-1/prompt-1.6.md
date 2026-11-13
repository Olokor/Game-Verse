# Prompt 1.6: Ludo in Unity

- **Status:** in progress.
- **Plan:** approved by the owner on 2026-11-13. Four parts, each committed when green.
- **ADR:** [ADR-0011](../../architecture/adr-0011-unity-client.md), filled in part by part.

## Goal
The Ludo game module in Unity, on the shared engine with no rule logic in Unity, plus the platform screens (lobby, matchmaking, private room, results).

**Acceptance:** two Android devices, or one device and the Unity editor, play a full private-room game through the real backend.

## Parts

| Part | What | Status | Commit |
|---|---|---|---|
| A | Client plumbing: API client, guest session, Android Keystore secure store, realtime connection, `GameHost` | Done | `9eb4eb7` |
| B | Ludo module: board, tokens, die drawn in code; transitions and animations; synthesised sounds; pass-and-play host | Done | `8e07f23` |
| C | Platform screens: Welcome, Home, Lobby, Matchmaking, Private room, Game (HUD), Results; router; new components | Done | `b863baf` |
| D | Android build, autoplay acceptance (editor done, phone pending), your playtest | **In progress** | |

## Part A: done (`9eb4eb7`)
- **What was built:**
  - `AppConfig`: the API URL, baked in at build time. The default is `http://127.0.0.1:8080` through `adb reverse`, and plain HTTP is allowed in development builds only.
  - `ApiClient` with problem+json errors.
  - `PlayerSession`: one-tap guest sign-in, then refresh-token or device-secret renewal.
  - `AndroidKeystoreStore`.
  - `RealtimeConnection`: pong, dropping stale states, backoff reconnects with fresh tickets, resending actions with the same seq (seqs start from the Unix time in milliseconds).
  - `GameHost`.
- **Verified:**
  - 31/31 EditMode tests;
  - the Android IL2CPP APK builds (85 MB development build).
- **Lesson:** an IL2CPP build takes about 25 minutes. Never wrap `make android` in a shell `timeout` (it killed the first build). Run it in the background.

## Part B: done (`8e07f23`)
- **What was built:**
  - `LudoBoardLayout`: the 15×15 board, with a 52-square track that cuts the four inner corners diagonally.
  - `LudoStateMapper` (the engine decides the legal moves), `LudoTransition` and `LudoController`.
  - Drawing in Painter2D; tokens carry their colour and a shape.
  - A tween helper instead of LitMotion (recorded in ADR-0011).
  - Synthesised sounds, haptics, and `LocalLudoHost`.
  - New tokens: `color.game.ludo.*` and `motion.game.*`.
- **Verified:** 39/39 EditMode tests, including a full pass-and-play game through the host.

## Part C: done (`b863baf`)
- **What was built:**
  - **Components** (`UI/Components/PlatformComponents.cs`): ModeTile, PlayerChip (shape glyph, optional online dot, turn bar), RoomCodeDisplay, Sheet, Toast, Skeleton.
  - **Shell** (`Platform/Shell/`): AppServices, AppScreen, Router (closes sheets, back-button order), DevLaunch, Autoplay.
  - **Screens** (`Platform/Screens/` plus `Platform/Resources/Screens/*.uxml`, `Screens.uss`): Welcome, Home, Lobby (with the house-rules sheet), Matchmaking, Room, Game (HUD), Results.
  - **Boot** builds the shell. The Boot scene is regenerated only when it is missing or wrong (no more git churn), and its camera now has an AudioListener. The mock `UI/Home` is removed.
  - **New tokens:** `color.bg.scrim`, `opacity.*`, `motion.toast-hold`.
- **How it was checked:**
  - `make unity-screens`, a new PlayMode screen tour against `make up`: 12 screenshots at 360×800 in `apps/mobile/TestResults/screens/`, reviewed by eye.
  - **First tour, fixed:** oversized HUD chips, a gap above the board, a blank die face, a leaked toast, an invented "0 playing" count, a squashed top bar, elliptical choice chips.
  - **The ui-reviewer agent:** 18 findings. Fixed: all 4 critical, all 5 high, and the cheap medium ones.
    - token touch area at least 44px, and larger shapes;
    - back closes sheets first;
    - "vs AI while you wait" no longer uses a disposed screen;
    - the lobby no longer opens twice;
    - loading and double-tap guards;
    - a reduced-motion fade;
    - "on" choices filled;
    - 44px targets;
    - the Home empty state;
    - left-aligned tile text;
    - a reason on the Start button;
    - a pressed die;
    - shape avatars;
    - opacity tokens.
  - **Deferred** (listed in ADR-0011): a Results celebration, a room QR code, tokenised pulse sizes and letter spacing, header icons.
  - EditMode tests: 42/42, including the router and house rules.
- **Problems met:**
  - the catalog answered in 5.7s once on a cold API (15ms warm), so the tour waits for the cards;
  - a heredoc with quotes broke the shell twice, so longer edit scripts now go to a scratchpad file.

## Part D: in progress (the editor side is done; the phone side is ready and waiting for a phone)
- **Done:**
  - **`make unity-accept`** (`Tests/PlayMode/AcceptanceTests.cs`): the real app autoplays through Welcome, Home, Lobby and Room, then plays a full private-room game against a bot seat through `make up`.
    - It **passed** on 2026-11-13 with session `01a1d5bc-0919-7d00-87c4-df8a8dcddcb6`, room `J3UFXR`.
    - Postgres shows it `finished`, `online_private`, with 297 validated moves and the reason `all_tokens_home`.
    - The app showed Results ("You came 2nd").
  - **`make device-accept`** (`infrastructure/scripts/device-accept.mjs`):
    1. installs the APK and sets up `adb reverse tcp:8080`;
    2. a bot opens a room;
    3. launches the app with `-e gv_autoplay_room CODE`;
    4. the bot plays the other seat;
    5. passes when the match ends and logcat shows `GV_ACCEPT session=...`.
  - `make device-check` now also forwards the API port (8080).
- **Next steps:**
  1. Connect the phone over USB (USB debugging on, accept the prompt).
  2. With `make up` running, run `make device-accept`. The APK is rebuilt with the new screens; if not, run `make android` first (about 25 minutes, in the background).
  3. The owner's manual playtest: create a room on the phone, join it from the editor (`GameVerse > Play` in the Boot scene) with the code, and play a game.
  4. Update ADR-0011 (part D), this file and the summary; commit part D.

## Decisions taken in this prompt (tech lead; recorded in ADR-0011)
- The board and dice are drawn in code (original art), and the sounds are synthesised.
- The built-in tween helper replaces LitMotion.
- Pass-and-play is fully offline.
- The API URL is baked in at build time.
