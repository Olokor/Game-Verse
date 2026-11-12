# GameVerse Build Plan: Prompts for Claude Code

How to use this file: copy one prompt at a time into Claude Code. Do not move to the next prompt until the acceptance checks pass and you have reviewed the result. Start every big prompt in plan mode (Shift+Tab) so Claude Code proposes before it writes.

---

## Setup before Prompt 1

1. Put these files at the repo root:
   - `CLAUDE.md`
   - `docs/game-design/GAME_BRIEFS.md`
   - `docs/design/DESIGN_SYSTEM.md`
   - `docs/PROMPTS.md` (this file)
2. Install on your workstation:
   - .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10` on Windows)
   - Docker Desktop, Node LTS, pnpm (`winget install pnpm.pnpm`)
   - Unity Hub + Unity 6 LTS with Android Build Support (OpenJDK, SDK, NDK). Unity Hub officially targets Ubuntu on Linux; on Fedora it generally works but expect occasional friction. iOS builds will need a Mac later.
   - An editor with C# support: JetBrains Rider (free for non-commercial use, best Unity integration) or VS Code with the C# Dev Kit.
3. Optional but very useful: a Unity MCP bridge so Claude Code can drive the Unity Editor (create GameObjects, run tests, read console). Search for the current maintained "unity-mcp" project and check it supports Unity 6 before installing.
4. Create the two subagents below in `.claude/agents/`.

### `.claude/agents/rules-auditor.md`
```markdown
---
name: rules-auditor
description: Reviews any change to shared/GameVerse.Rules against GAME_BRIEFS.md and the golden fixtures. Use after any rules or physics change.
tools: Read, Grep, Glob, Bash
---
You audit game rules for GameVerse. All rules live in shared/GameVerse.Rules and are used by both server and Unity.
1. Read the relevant section of docs/game-design/GAME_BRIEFS.md.
2. Read the diff and the fixtures in shared/rules-fixtures/<game>/.
3. List any rule from the brief that has no fixture covering it.
4. Run `dotnet test shared/GameVerse.Rules.Tests` and report results.
5. Check the library constraints from CLAUDE.md: netstandard2.1, C# 9 only, zero dependencies, no float/double in deterministic simulation, no System.Random, no DateTime.Now, no dictionary-order dependence.
6. Look for: client-trusted state, hidden info leaking through ViewFor, missing turn/timeout handling, off-by-one board logic, mutable state escaping Apply.
Report findings as a numbered list with file:line references. Do not edit files.
```

### `.claude/agents/ui-reviewer.md`
```markdown
---
name: ui-reviewer
description: Reviews UI changes (React or UI Toolkit) against DESIGN_SYSTEM.md. Use after building or changing any screen or component.
tools: Read, Grep, Glob, Bash
---
You review GameVerse UI against docs/design/DESIGN_SYSTEM.md.
Check: hardcoded colors/sizes/durations instead of tokens; missing pressed/disabled/loading/empty/error states; touch targets under 44px; contrast; motion durations; more than one primary action; layout at 360x800 and 1440x900.
If Playwright is available, take screenshots at both sizes and inspect them.
Report findings as a prioritized list. Do not edit files.
```

---

## Phase 0: Foundation (week 1 to 2)

### Prompt 0.1: Repo inspection and plan
```
Read CLAUDE.md, docs/game-design/GAME_BRIEFS.md and docs/design/DESIGN_SYSTEM.md fully.
Then inspect this repository: list what exists, what the README says, and what conflicts with CLAUDE.md.
Produce:
1. A gap list between the repo and the layout in CLAUDE.md sections 4.1 and 5.
2. Any foundational decisions still missing (list them as questions for me).
3. ADR-0001 in docs/architecture/ recording the stack decisions from CLAUDE.md section 3, including why C# end to end and why raw WebSockets instead of SignalR.
Do not write application code yet.
```

### Prompt 0.2: Shared libraries skeleton (do this before the backend)
```
Plan mode. Create the shared libraries per CLAUDE.md section 3 "Shared libraries" and 4.1:
- shared/GameVerse.Rules (netstandard2.1, LangVersion 9.0, no package references)
- shared/GameVerse.Contracts (netstandard2.1, LangVersion 9.0)
- shared/GameVerse.Rules.Tests (net10.0, xUnit, Shouldly)
In Rules, implement the foundations every game needs:
- Fixed-point number type in GameVerse.Rules.Math with add/sub/mul/div, sqrt, comparison, conversion for display only
- Seeded deterministic PRNG (e.g. xoshiro256**) with commit-reveal helpers (seed hash via SHA-256)
- IGameEngine<TState,TAction,TConfig>, ApplyResult, GameOutcome, SeatInfo as in CLAUDE.md 4.2
- A fixture loader in the test project that reads shared/rules-fixtures/<game>/*.json
In Contracts: SessionManifest, realtime envelope types, GameEvent types.
Add a build check that fails if LangVersion above 9 or any PackageReference appears in GameVerse.Rules.
Add `make rules` that builds Release and copies the DLLs into apps/mobile/Assets/GameVerse/Plugins/Shared/ (create the folder if Unity is not set up yet).
Acceptance: dotnet test passes, including tests proving the PRNG gives identical sequences for the same seed and the fixed-point math is exact for a set of known values.
```

### Prompt 0.3: Backend skeleton
```
Plan mode. Create the .NET backend per CLAUDE.md 4.1:
- backend/GameVerse.slnx (including the shared/ projects) with Directory.Build.props (nullable, warnings as errors, analyzers) and Directory.Packages.props (central versions)
- GameVerse.Api, GameVerse.Platform (empty module folders with a README each), GameVerse.Infrastructure, and the three test projects
- Serilog JSON logging, OpenAPI + Scalar in Development, health checks for Postgres and Redis at /health
- A WebSocket echo endpoint at /ws/echo
- EF Core DbContext with an initial empty migration, Hangfire with Postgres storage and its dashboard behind an admin-only policy (dev: localhost only)
- docker-compose.yml with postgres, redis, api, worker (same image, Hangfire server role via env var)
- Multi-stage Dockerfile, non-root runtime
- .env.example, Makefile (up, test, rules, migrate, schema)
- NetArchTest rule: GameVerse.Rules must not reference anything; modules may not reference each other's internals
Acceptance: `make up` starts everything, `curl localhost:<port>/health` is healthy for db and redis, an integration test (Testcontainers) connects to /ws/echo and gets the echo, `make test` passes.
```

### Prompt 0.4: Design tokens package
```
Plan mode. Create packages/design-tokens/ with tokens.json containing every value in docs/design/DESIGN_SYSTEM.md sections 2 to 5.
Write build.mjs (Node, no dependencies) that outputs:
- apps/web/src/styles/tokens.css and apps/admin/src/styles/tokens.css as CSS custom properties
- apps/mobile/Assets/GameVerse/UI/Tokens.uss as USS variables
Add `make tokens`. Add a check that fails if any token in the JSON is missing from any output.
```

### Prompt 0.5: Web shell skeleton
```
Plan mode. Create apps/web with React 19, TypeScript, Vite, Tailwind v4 wired to tokens.css, Radix, Motion, TanStack Query, Zustand, React Router.
Load Bricolage Grotesque and Manrope (open source, self-hosted; replaced Clash Display and Satoshi, see ADR-0003). Build the Button, GameCard (hero/standard/compact), CurrencyPill, XPBar, PlayerChip and Skeleton components from DESIGN_SYSTEM.md section 6, with every state.
Create a /dev/components page that shows every component and state.
Build the Home screen layout from DESIGN_SYSTEM.md section 7 using static mock catalog data clearly marked TODO(gameverse) until the API exists.
Then run the ui-reviewer agent and fix what it finds.
Acceptance: looks premium at 360x800 and 1440x900, no hardcoded colors, Vitest passes.
```

### Prompt 0.6: Unity project skeleton
```
Plan mode. Create the Unity 6 project at apps/mobile:
- URP, Input System, Addressables, Localization, UI Toolkit, UniTask, NativeWebSocket, com.unity.nuget.newtonsoft-json
- Folder layout: Assets/GameVerse/{Core,Platform,UI,Net,Plugins/Shared,Games/<Game>}
- Run `make rules` and confirm Unity loads GameVerse.Rules.dll and GameVerse.Contracts.dll with no errors; add link.xml preserving both
- Assembly definitions: GameVerse.Core, GameVerse.Platform, GameVerse.Net, one per game, all referencing the shared DLLs
- IGameModule and IGameHost exactly as in CLAUDE.md 4.2, using SessionManifest from Contracts
- Newtonsoft converters so Contracts types round-trip with the server's System.Text.Json output (test with a JSON sample produced by the backend)
- Tokens.uss imported and a UI Toolkit Button + GameCard that match the web versions
- A Boot scene that loads the Home UI document
- One EditMode test that calls into GameVerse.Rules (e.g. the PRNG) and asserts the same sequence as the .NET test
Give me the exact command to run Unity tests headless on Linux.
Acceptance: an IL2CPP Android build succeeds and the PRNG test produces identical values on device and in dotnet test.
```

### Prompt 0.7: Web performance spike (decision gate)
```
Build a throwaway Unity Web build: a 2D scene with 200 animated sprites plus one small 3D scene (a car on a track with baked lighting).
Embed it in apps/web using react-unity-webgl on a /spike route.
Report: build size (Brotli), load time, and give me a checklist to test it on a real iPhone (Safari) and a low-end Android (Chrome).
After I report back the numbers, write ADR-0002 deciding which game types ship on web.
```


### Prompt 0.8: Error-reporting baseline (owner-approved addition, 2026-11-11)
```
Add error reporting before Phase 1 so every feature starts with it. Sentry SDKs everywhere, sending to self-hosted GlitchTip
(docker compose profile, `make errors-up` / `make errors-setup`). API: global ProblemDetails exception handling (no exception
detail in responses), env-gated Sentry + Serilog sink, no PII. Web: designed error screen, error boundary + router errorElement,
reporting module. Unity: Sentry Unity SDK configured from ProjectSetup, on-device check. Tests for each, plus an end-to-end check
that events arrive in GlitchTip. Record the decision as ADR-0005.
```
---

## Phase 1: Platform core + Ludo vertical slice (week 3 to 7)

### Prompt 1.1: Accounts and auth
```
Plan mode. Implement the Accounts module: guest account creation (device-bound), upgrade guest to email account, login, JWT access + rotating hashed refresh tokens, logout, profile basics (display name, avatar id).
Rate limit auth endpoints. Export OpenAPI to packages/api-schema and generate the TS client with `make schema`.
Integration tests (Testcontainers) for every flow including guest upgrade keeping progress and refresh token reuse detection.
```

### Prompt 1.2: Catalog and sessions
```
Implement Catalog (game registry with modes, category, accent color, feature flags) seeded with all 11 games from CLAUDE.md, only Ludo enabled.
Implement Sessions: GameSession, SessionPlayer, MoveLog (append-only), MatchResult. Expose the SessionManifest endpoint using the Contracts type.
Replace the web Home mock data with the real catalog API.
```

### Prompt 1.3: Ludo rules engine (shared library)
```
Plan mode. Implement shared/GameVerse.Rules/Ludo per GAME_BRIEFS.md section 1, including every house rule toggle, implementing IGameEngine.
Dice come from the shared seeded PRNG with commit-reveal: seed hash published at game creation, seed revealed at the end.
Write fixtures in shared/rules-fixtures/ludo/ covering every rule and toggle.
Also implement Ludo AI levels 1 to 3 from the brief inside the shared library.
Run the rules-auditor agent and close every gap it reports. Then run `make rules`.
```

### Prompt 1.4: Realtime play + private rooms
```
Plan mode. Implement the realtime protocol from CLAUDE.md 4.5 and private rooms from 4.6:
- Single-use 30s WebSocket tickets
- MatchHost per live match (single-reader Channel), Redis snapshot after every action, Redis pub/sub fan-out
- Room create/join by code, seats, ready states, house rules set by host
- Server applies Ludo actions through the shared engine, broadcasts ViewFor(seat) with stateVersion
- Turn timer with auto-move on timeout, reconnect with snapshot, disconnect grace and forfeit, heartbeat
- Best of three and rematch at room level
Write an integration test that runs two simulated WebSocket clients through a full Ludo game to completion, plus one that kills the MatchHost mid-game and resumes from Redis.
```

### Prompt 1.5: Matchmaking
```
Implement Redis matchmaking queues for Ludo 2p and 4p as a hosted service, Glicko-2 ratings (tested against reference values), widen rating band over time, fill with AI after a timeout only in casual mode. Tests with simulated players.
```

### Prompt 1.6: Ludo in Unity
```
Plan mode. Build the Ludo game module in Unity using the shared GameVerse.Rules Ludo engine (no rule logic in Unity):
- Board, tokens, dice with the feel described in GAME_BRIEFS.md section 1 (dice tumble, hop animation, capture launch)
- Modes: local and vs AI run the shared engine locally; online goes through IGameHost and the server
- Platform screens in UI Toolkit: lobby, matchmaking, private room, results
Acceptance: two Android devices (or one device + Unity editor) play a full private-room game through the real backend.
```

### Prompt 1.7: Ludo on web
```
Produce the Ludo Web build and integrate it into apps/web game lobby and play route via react-unity-webgl, with the same SessionManifest bridge.
Playwright test: two browser contexts create and join a room and play until the first capture.
```

**Milestone 1 demo:** Player A and B register, A creates a room, B joins with a code, they play Ludo across phone and browser, server validates, winner shown, rematch works.

---

## Phase 2: Progression, social and admin (week 8 to 11)

### Prompt 2.1: Progression
```
Implement Progression per GAME_BRIEFS.md section 13: account XP and levels, game mastery, achievements, daily and weekly quests (Hangfire recurring job at midnight WAT), login streak with weekly freeze. Games emit events only; Progression grants rewards via in-process domain events. Results screen shows XP and quest progress with count-up animation.
```

### Prompt 2.2: Economy
```
Implement the coin ledger (append-only, idempotent by request key, balance derived and cached in Redis), cosmetics catalog, inventory, equip slots, and a coins-only store. Ludo dice skins and board themes as the first cosmetics.
```

### Prompt 2.3: Social and leaderboards
```
Friends (request, accept, remove, block), presence, invite-to-room push and in-app banner, leaderboards (Redis sorted sets; global, friends, country; daily/weekly/season) with Postgres snapshots.
```

### Prompt 2.4: Admin console
```
Plan mode. Create apps/admin (same stack and tokens as web) and the Admin module per CLAUDE.md "Admin console":
player search, match history with move log viewer, ban/mute, currency grant/reverse through the ledger with mandatory reason, quest and tournament editing, feature flags. Every write is audit-logged. Admin role required.
Run ui-reviewer afterwards.
```

---

## Phase 3: More turn-based games (week 12 to 19)

Each game follows the Ludo pattern: shared engine + fixtures + AI, rules-auditor, realtime integration, Unity presentation, web build.

- **Prompt 3.1 Whot:** emphasize `ViewFor` hiding hands, server shuffle via shared PRNG, Last Card / Check Up enforcement, ruleset presets as data. Ask me for the "Lagos Rules" preset before implementing.
- **Prompt 3.2 Draughts:** one engine with variant flags for 8x8 and 10x10 international, alpha-beta AI in the shared library.
- **Prompt 3.3 Chess:** bitboard move generator in the shared library verified by perft fixtures, server clocks, low-level AI in the shared library, Stockfish in its own container behind an adapter in Infrastructure for strong AI and analysis.

---

## Phase 4: Arcade and score verification (week 20 to 25)

- **Prompt 4.1 Twin Lanes:** deterministic simulation and verifier in the shared library, Unity renders it. Score submission with seed + tap log, server replays with the same verifier, Daily Run.
- **Prompt 4.2 Wahala:** data-driven trap kit, 30 levels in 3 worlds, ghost deaths service (aggregate death positions per level), share card. Level completion verifier in the shared library for featured levels.

## Phase 5: Physics games (week 26 to 35)

- **Prompt 5.1 Pool physics core:** fixed-point deterministic simulation in the shared library, fixtures with shot parameters and expected final positions, a test that runs the same fixtures inside a Unity IL2CPP build on device and compares. This is the riskiest piece; build and prove it before any visuals.
- **Prompt 5.2 Pool game:** rules, UI, online play.
- **Prompt 5.3 Downhill:** physics rig (Unity physics for feel), ghost recording, time verification by plausibility checks. Discuss with me before attempting full deterministic replay.

## Phase 6: Action games (week 36+)

- **Prompt 6.1 Stickman Combat:** combat system, enemy AI roster, chapter 1.
- **Prompt 6.2 Fabulous Super Race:** only after we finish the design workshop on the open questions in GAME_BRIEFS.md section 10.
- **Prompt 6.3 Rival Realms:** only after the concept, roster and art direction are approved.

## Phase 7: Retention layer

Ayo, Daily Word, Penalty Kings, Live Trivia Night, clubs, tournaments, season pass.

---

## Rules of thumb while working with Claude Code

- One prompt per session. Run `/clear` between phases so context stays sharp; CLAUDE.md reloads automatically.
- If Claude Code says something works, ask for the command and output.
- After every UI prompt, run the ui-reviewer. After every rules prompt, run the rules-auditor, then `make rules` so Unity has the new DLL.
- Commit after every passing prompt. Small commits make rollback painless.
- When a prompt grows past one session, ask Claude Code to split it and update this file.
