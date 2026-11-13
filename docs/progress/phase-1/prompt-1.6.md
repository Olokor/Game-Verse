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
| C | Platform screens: Welcome, Home, Lobby, Matchmaking, Private room, Game (HUD), Results; router; new components | **In progress** | |
| D | Android build, autoplay device acceptance against the backend, screenshots and ui-reviewer, your playtest | Not started | |

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

## Part C: in progress
- **Written so far** (all compiling, 39/39 tests still passing):
  - **Components** (`UI/Components/PlatformComponents.cs`): ModeTile, PlayerChip, RoomCodeDisplay, Sheet, Toast, Skeleton; their styles in `Components.uss`; a new `color.bg.scrim` token.
  - **Shell** (`Platform/Shell/`):
    - `AppServices` (session, router, connections, toasts);
    - `AppScreen` (base class);
    - `Router` (stack, 240ms transitions);
    - `DevLaunch` (autoplay launch extra);
    - `Autoplay`.
  - **Screens** (`Platform/Screens/` plus `Platform/Resources/Screens/*.uxml`, `Screens.uss`): Welcome, Home (real catalog), Lobby (modes, players, AI level, best of, house rules sheet, join by code), Matchmaking (polls every second), Room (live lobby socket, ready, start, rules, copy and share), Game (HUD: chips, turn clock, reconnect banner, leave), Results.
  - **Boot** now builds the shell. `ProjectSetup` only rebuilds the Boot scene when it is missing or wrong (this ends the scene churn), and the mock `UI/Home/Home.uxml` is gone.
- **Next steps:**
  1. Run `make unity-setup`, then a play test in the editor against `make up`: does UI Toolkit get taps without an EventSystem?
  2. Capture screenshots at 360×800 and run ui-reviewer.
  3. EditMode tests for the router and the house-rules mapping.
  4. Update ADR-0011 (part C), the Unity README and this file; commit part C.

## Part D: not started
1. `make android`, and add `adb reverse tcp:8080` to device-check.
2. The autoplay acceptance: `adb shell am start ... -e gv_autoplay_room CODE`, with the Node bot as the other seat. Look for `GV_ACCEPT` in logcat.
3. An opt-in editor PlayMode test against the backend.
4. Your manual playtest.

**Needs:** a phone connected over USB.

## Decisions taken in this prompt (tech lead; recorded in ADR-0011)
- The board and dice are drawn in code (original art), and the sounds are synthesised.
- The built-in tween helper replaces LitMotion.
- Pass-and-play is fully offline.
- The API URL is baked in at build time.
