# GameVerse Build Plan: Prompts for Claude Code

How to use this file: copy one prompt at a time into Claude Code. Do not move to the next prompt until the acceptance checks pass and you have reviewed the result. Start every big prompt in plan mode (Shift+Tab) so Claude Code proposes before it writes.

---

## Setup before Prompt 1

1. Put these files at the repo root:
   - `CLAUDE.md`
   - `docs/game-design/GAME_BRIEFS.md`
   - `docs/design/DESIGN_SYSTEM.md`
   - `docs/PROMPTS.md` (this file)
2. Install on your workstation: Docker, Python 3.12+, Node 22 LTS, Unity Hub + Unity 6 LTS with Android Build Support (OpenJDK, SDK, NDK).
   - Unity Hub officially targets Ubuntu on Linux. On Fedora it generally works but expect occasional friction. iOS builds will need a Mac later.
3. Optional but very useful: a Unity MCP bridge so Claude Code can drive the Unity Editor (create GameObjects, run tests, read console). Search for the current maintained "unity-mcp" project and check it supports Unity 6 before installing.
4. Create the two subagents below in `.claude/agents/`.

### `.claude/agents/rules-auditor.md`
```markdown
---
name: rules-auditor
description: Reviews any change to a game rules engine (Python or C#) against GAME_BRIEFS.md and the shared rules fixtures. Use after any engine change.
tools: Read, Grep, Glob, Bash
---
You audit game rules engines for GameVerse.
1. Read the relevant section of docs/game-design/GAME_BRIEFS.md.
2. Read the engine diff and the fixtures in shared/rules-fixtures/<game>/.
3. List any rule from the brief that has no fixture covering it.
4. Run the Python fixture tests and report results.
5. Look for: client-trusted state, RNG on the client, hidden info leaking to the wrong seat, missing turn/timeout handling, off-by-one board logic.
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
1. A gap list between the repo and CLAUDE.md section 5 layout.
2. Any foundational decisions still missing (list them as questions for me).
3. ADR-0001 in docs/architecture/ recording the stack decisions from CLAUDE.md section 3.
Do not write application code yet.
```

### Prompt 0.2: Backend skeleton
```
Plan mode. Create the Django backend skeleton per CLAUDE.md:
- backend/ with config split into base/dev/prod settings, ASGI with Channels routing
- Apps created empty but registered: accounts, profiles, catalog, sessions, engines (plain package, no Django), matchmaking, realtime, social, progression, economy, leaderboards, notifications
- docker-compose.yml with postgres, redis, api (uvicorn), worker, beat
- .env.example, Makefile with up/test/lint targets, ruff + mypy + pytest configured
- A /api/health endpoint that checks DB and Redis, and a WebSocket echo consumer at /ws/echo/
Acceptance: `make up` starts everything, `curl /api/health` returns ok for db and redis, a test connects to /ws/echo/ and gets the echo back, `make test` passes.
```

### Prompt 0.3: Design tokens package
```
Plan mode. Create packages/design-tokens/ with tokens.json containing every value in docs/design/DESIGN_SYSTEM.md sections 2 to 5.
Write a Python generator script (no heavy deps) that outputs:
- apps/web/src/styles/tokens.css as CSS custom properties
- apps/mobile/Assets/GameVerse/UI/Tokens.uss as USS variables
Add `make tokens`. Add a test that fails if any token in the JSON is missing from either output.
```

### Prompt 0.4: Web shell skeleton
```
Plan mode. Create apps/web with React 19, TypeScript, Vite, Tailwind v4 wired to tokens.css, Radix, Motion, TanStack Query, Zustand, React Router.
Load Clash Display and Satoshi. Build the Button, GameCard (hero/standard/compact), CurrencyPill, XPBar, PlayerChip and Skeleton components from DESIGN_SYSTEM.md section 6, with every state.
Create a /dev/components page that shows every component and state.
Build the Home screen layout from DESIGN_SYSTEM.md section 7 using static mock catalog data clearly marked TODO(gameverse) until the API exists.
Then run the ui-reviewer agent and fix what it finds.
Acceptance: looks premium at 360x800 and 1440x900, no hardcoded colors, Vitest passes.
```

### Prompt 0.5: Unity project skeleton
```
Plan mode. Create the Unity 6 project at apps/mobile:
- URP, Input System, Addressables, Localization, UI Toolkit
- Folder layout: Assets/GameVerse/{Core,Platform,UI,Net,Games/<Game>}
- Assembly definitions: GameVerse.Core, GameVerse.Platform, GameVerse.Net, one per game
- IGameModule and IGameHost interfaces and SessionManifest types exactly as in CLAUDE.md 4.2
- Tokens.uss imported and a UI Toolkit Button + GameCard that match the web versions
- A Boot scene that loads the Home UI document
- EditMode test assembly with one passing test, runnable via batchmode
Give me the exact command to run tests headless on Windows (my dev machine, see ADR-0002).
```

### Prompt 0.6: Web performance spike (decision gate)
```
Build a throwaway Unity Web build: a 2D scene with 200 animated sprites plus one small 3D scene (a car on a track with baked lighting).
Embed it in apps/web using react-unity-webgl on a /spike route.
Report: build size (Brotli), load time, and give me a checklist to test it on a real iPhone (Safari) and a low-end Android (Chrome).
After I report back the numbers, write ADR-0003 deciding which game types ship on web.
```

---

## Phase 1: Platform core + Ludo vertical slice (week 3 to 7)

### Prompt 1.1: Accounts and auth
```
Plan mode. Implement accounts: guest account creation (device-bound), upgrade guest to email account, login, JWT with rotating refresh, logout, profile basics (display name, avatar id).
Rate limit auth endpoints. Export OpenAPI with drf-spectacular. Generate the TS client into packages/api-schema and wire `make schema`.
Tests for every flow including guest upgrade keeping progress.
```

### Prompt 1.2: Catalog and sessions
```
Implement catalog (game registry with modes, category, accent color, feature flags) seeded with all 11 games from CLAUDE.md, only Ludo enabled.
Implement sessions: GameSession, SessionPlayer, MoveLog (append-only), MatchResult. Expose the SessionManifest endpoint.
Replace the web Home mock data with the real catalog API.
```

### Prompt 1.3: Ludo rules engine (Python, canonical)
```
Plan mode. Implement backend/engines/ludo as pure Python per GAME_BRIEFS.md section 1, including every house rule toggle.
API: new_game(config, seats, seed) -> State, legal_moves(state, seat) -> list, apply(state, action) -> State, outcome(state).
Dice via commit-reveal: store seed hash at creation, reveal at end.
Write fixtures in shared/rules-fixtures/ludo/ covering every rule and toggle, plus a pytest runner that loads them.
Run the rules-auditor agent and close every gap it reports.
```

### Prompt 1.4: Realtime play + private rooms
```
Plan mode. Implement the realtime protocol from CLAUDE.md 4.5 and private rooms from 4.6:
- Single-use WS tickets
- Room create/join by code, seats, ready states, house rules set by host
- Server applies Ludo actions via the engine, broadcasts per-seat state with stateVersion
- Turn timer with auto-move on timeout, reconnect with snapshot, disconnect grace and forfeit
- Best of three and rematch at room level
Write an integration test that runs two simulated clients through a full Ludo game to completion.
```

### Prompt 1.5: Matchmaking
```
Implement Redis matchmaking queues for Ludo 2p and 4p, Glicko-2 ratings, widen rating band over time, fill with AI after a timeout only in casual mode. Tests with simulated players.
```

### Prompt 1.6: Ludo in Unity
```
Plan mode. Build the Ludo game module in Unity:
- C# mirror of the rules in Games/Ludo/Rules, passing the same shared fixtures in EditMode tests
- Board, tokens, dice with the feel described in GAME_BRIEFS.md section 1 (dice tumble, hop animation, capture launch)
- Modes: local, vs AI (3 levels), online via the platform IGameHost
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

## Phase 2: Progression and social (week 8 to 10)

### Prompt 2.1: Progression
```
Implement progression per GAME_BRIEFS.md section 13: account XP and levels, game mastery, achievements, daily and weekly quests (Celery beat rotation at midnight WAT), login streak with weekly freeze. Games emit events only; progression grants rewards. Results screen shows XP and quest progress with count-up animation.
```

### Prompt 2.2: Economy
```
Implement the coin ledger (append-only transactions, balance derived and cached), cosmetics catalog, inventory, equip slots, and a coins-only store. Ludo dice skins and board themes as the first cosmetics.
```

### Prompt 2.3: Social and leaderboards
```
Friends (request, accept, remove, block), presence, invite-to-room push and in-app banner, leaderboards (Redis sorted sets; global, friends, country; daily/weekly/season) with Postgres snapshots.
```

---

## Phase 3: More turn-based games (week 11 to 18)

Each game follows the same pattern as Ludo: engine + fixtures, rules-auditor, realtime integration, Unity module, web build.

- **Prompt 3.1 Whot:** emphasize per-seat hidden state, server shuffle, Last Card / Check Up enforcement, ruleset presets as data. Ask me for the "Lagos Rules" preset before implementing.
- **Prompt 3.2 Draughts:** one engine with variant flags for 8x8 and 10x10 international.
- **Prompt 3.3 Chess:** python-chess for validation, server clocks, Stockfish in its own container for AI and analysis.

---

## Phase 4: Arcade and score verification (week 19 to 24)

- **Prompt 4.1 Twin Lanes:** deterministic seeded simulation in C# and Python, score submission with seed + tap log, server re-simulation, Daily Run.
- **Prompt 4.2 Wahala:** data-driven trap kit, 30 levels in 3 worlds, ghost deaths service (aggregate death positions per level), share card.

## Phase 5: Physics games (week 25 to 34)

- **Prompt 5.1 Pool physics core:** fixed-point deterministic engine in C#, Python port, fixtures with shot parameters and expected final positions. This is the riskiest piece; build and test it before any visuals.
- **Prompt 5.2 Pool game:** rules, UI, online play.
- **Prompt 5.3 Downhill:** physics rig, 20 tracks, ghosts.

## Phase 6: Action games (week 35+)

- **Prompt 6.1 Stickman Combat:** combat system, enemy AI roster, chapter 1.
- **Prompt 6.2 Fabulous Super Race:** only after we finish the design workshop on the open questions in GAME_BRIEFS.md section 10.
- **Prompt 6.3 Rival Realms:** only after the concept, roster and art direction are approved.

## Phase 7: Retention layer

Ayo, Daily Word, Penalty Kings, Live Trivia Night, clubs, tournaments, season pass.

---

## Rules of thumb while working with Claude Code

- One prompt per session. Run `/clear` between phases so context stays sharp; CLAUDE.md reloads automatically.
- If Claude Code says something works, ask for the command and output.
- After every UI prompt, run the ui-reviewer. After every engine prompt, run the rules-auditor.
- Commit after every passing prompt. Small commits make rollback painless.
- When a prompt grows past one session, ask Claude Code to split it and update this file.
