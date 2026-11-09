# GameVerse: Project Context for Claude Code

Read this file at the start of every session. It is the source of truth for how this project is built. If a request conflicts with this file, stop and ask before proceeding.

Supporting docs (read when relevant):
- `docs/game-design/GAME_BRIEFS.md` for game rules, modes and scope
- `docs/design/DESIGN_SYSTEM.md` for every UI decision
- `docs/PROMPTS.md` is the phase plan the owner works from

---

## 1. What GameVerse is

A cross-platform gaming platform (Android first, web second, iOS later) where many games share one account, one wallet of soft currency, one friends list, one matchmaking system, one leaderboard and reward system.

The launch library, in build order:

| # | Game | Genre | Multiplayer model |
|---|------|-------|-------------------|
| 1 | Ludo | Board | Turn-based, server-authoritative, 2 to 4 players |
| 2 | Whot | Card (Nigerian) | Turn-based, server-authoritative, hidden hands |
| 3 | Draughts (Checkers) | Board | Turn-based, server-authoritative |
| 4 | Chess | Board | Turn-based, server-authoritative, clocks, rating |
| 5 | Twin Lanes (2 Cars style) | Arcade | Single player + async leaderboards |
| 6 | Wahala (Die Again style troll platformer) | Platformer | Single player + level sharing |
| 7 | 8-Ball Pool | Physics / sports | Turn-based, deterministic physics, server-verified |
| 8 | Downhill (bicycle) | Physics racer | Single player + ghost races |
| 9 | Stickman Combat | Beat 'em up | Single player campaign |
| 10 | Fabulous Super Race | 3D arcade racer | Single player + ghosts, real-time later |
| 11 | Rival Realms (original fighter) | 2.5D fighting | Local/AI first, online later |

Never use third-party trademarks in names, assets, store text or code identifiers (no "Mortal Kombat", "Ketchapp", "Miniclip", "8 Ball Pool"). Mechanics can be inspired by existing games. Names, characters, art and audio must be original.

## 2. Your role and working rules

You are the senior engineer and tech lead. The owner (Moses) reviews and decides product questions. He writes Python first, Java second, and is comfortable with Linux, Docker and Traefik.

1. Inspect before you change. Read the relevant files and existing patterns first.
2. Use plan mode for anything touching more than ~3 files. Present the plan, wait for approval.
3. Work in small, testable increments. One feature per session where possible.
4. Never fake it. No placeholder logic presented as finished, no mocked results claimed as working. If something is stubbed, it says `TODO(gameverse):` with a reason and it is listed in the session summary.
5. Never claim something works unless you ran it. Show the command and the output.
6. Ask before any decision that changes product scope, the tech stack, data models shared across games, or anything with money.
7. Every session ends with: what changed, files touched, how to run it, how to test it, open TODOs.
8. Keep `README.md` and `docs/` current in the same commit as the change.
9. Conventional commits (`feat(ludo): ...`, `fix(matchmaking): ...`).
10. One branch per phase of `docs/PROMPTS.md`, created from `main`: `phase-0-foundation`, `phase-1-ludo-slice`, `phase-2-progression-social`, `phase-3-turn-based-games`, `phase-4-arcade`, `phase-5-physics`, `phase-6-action`, `phase-7-retention`. Commit on the phase branch after every passing prompt. Merge into `main` only when the phase's acceptance checks pass and the owner approves. Never commit directly to `main`. Check the current branch at the start of every session.

## 3. Locked technical decisions

Change any of these only with explicit approval.

### Backend (Python)
- Django 5.x, Django REST Framework, Django Channels (WebSockets), channels-redis
- PostgreSQL 16, Redis 7
- Celery + Redis for scheduled work (tournaments, daily quests, season resets)
- Auth: JWT via `djangorestframework-simplejwt` (access 15 min, rotating refresh). Guest accounts allowed and upgradable to full accounts.
- `drf-spectacular` for OpenAPI. Clients are generated from the schema, never hand-written:
  - TypeScript: `openapi-typescript` + `openapi-fetch`
  - C#: NSwag, run from a script, output committed to the Unity project
- Tests: `pytest`, `pytest-django`, `factory_boy`. Rules engines need near-total branch coverage.
- Lint/format: `ruff`, `mypy` on rules engines and services.
- Observability: structured JSON logging, Sentry (env-gated).

### Mobile (Unity)
- Unity 6 LTS, C#, URP. 2D Renderer for 2D games, Forward+ for 3D games.
- One Unity project. The platform shell and every game live in it. Games are separate assemblies (`.asmdef`) and Addressables groups so they can be downloaded on demand.
- Platform UI is built with **UI Toolkit** (UXML + USS), not uGUI. These are text files, which keeps them reviewable and generatable. Use uGUI only inside a game where world-space UI demands it.
- Animation: LitMotion for code tweens, **Rive** for designed motion (shared with web).
- Async: UniTask.
- Networking to backend: REST via generated client, realtime via `NativeWebSocket`.
- Input System package, Unity Localization package, Addressables, Cinemachine for 3D games.
- Tests: Unity Test Framework (EditMode for rules/logic, PlayMode for flows). Run headless with `-batchmode -runTests`.
- Scenes stay minimal. Prefer prefabs and code-driven setup, created through editor scripts where possible, so changes are diffable.

### Web
- React 19 + TypeScript + Vite
- Tailwind CSS v4, Radix UI primitives, Motion (Framer Motion) for transitions, Rive web runtime
- TanStack Query for server state, Zustand for client state, React Router
- Unity games embedded with `react-unity-webgl`. Each game ships its own small Web build, loaded only when played.
- Tests: Vitest + Testing Library, Playwright for end-to-end flows.

### Shared design tokens
`packages/design-tokens/tokens.json` is the single source for color, type, spacing, radius, motion. A build script generates:
- `apps/web/src/styles/tokens.css` (CSS variables consumed by Tailwind)
- `apps/mobile/Assets/GameVerse/UI/Tokens.uss` (USS variables)

Never hardcode a color, font size, radius or duration in components. If a token is missing, add it to `tokens.json`.

### Infrastructure
- Docker Compose for local dev: `postgres`, `redis`, `api` (Django ASGI via Uvicorn), `worker` (Celery), `beat`, `web`.
- Dev machine: Windows 11 + Docker Desktop (WSL2 backend). `make` targets must work from Git Bash and PowerShell.
- Tooling: `uv` for Python, `pnpm` workspace for Node, GitHub Actions for CI, Git LFS for binary assets, LF line endings via `.gitattributes`. See ADR-0002.
- Production: containers behind Traefik with Cloudflare in front. Environment-based config via `.env` files that are never committed. `.env.example` is always current.
- Modular monolith. No microservices. The one planned exception is a dedicated real-time game server for twitch-action PvP (fighting, live racing), which is a later phase and needs approval.

## 4. Architecture

### 4.1 Backend apps
```
backend/
  config/            settings (base/dev/prod), asgi, urls, routing
  accounts/          users, guest accounts, auth
  profiles/          avatars, frames, titles, stats
  catalog/           game registry: id, category, modes, versions, feature flags
  sessions/          game sessions, match records, move logs, replays
  engines/           pure Python rules engines, one package per game, NO Django imports
  matchmaking/       queues, rating bands, private rooms, room codes
  realtime/          Channels consumers, protocol, presence
  social/            friends, invites, clubs
  progression/       XP, levels, mastery, achievements, quests, season pass
  economy/           soft currency ledger, cosmetics inventory, store catalog
  tournaments/       brackets, schedules, prizes (non-monetary for MVP)
  leaderboards/      Redis sorted sets + periodic Postgres snapshots
  notifications/     in-app + push (FCM)
  moderation/        reports, chat filter, bans
```

### 4.2 Game Integration Contract

A game never handles auth, matchmaking, rewards or leaderboards itself. It receives a **SessionManifest** and emits **GameEvents**.

SessionManifest (platform to game):
```json
{
  "gameId": "ludo",
  "gameVersion": "1.0.0",
  "sessionId": "uuid",
  "mode": "online_private | online_ranked | local | vs_ai | solo",
  "localPlayerId": "uuid",
  "players": [{ "id": "uuid", "displayName": "...", "seat": 0, "cosmetics": {...}, "isAI": false }],
  "config": { "variant": "...", "houseRules": {...}, "aiLevel": 3 },
  "initialState": { ... },
  "realtime": { "wsUrl": "...", "ticket": "short-lived token" }
}
```

GameEvents (game to platform): `ready`, `action_submitted`, `state_applied`, `match_ended`, `player_left`, `error`, plus `telemetry` for analytics. In Unity these go through an `IGameHost` interface. On web they cross the Unity/React bridge as JSON messages with the same schema.

Every game implements `IGameModule` in C#:
```csharp
public interface IGameModule {
    string GameId { get; }
    UniTask LoadAsync(SessionManifest manifest, IGameHost host);
    void ApplyServerState(GameStateEnvelope envelope);
    UniTask UnloadAsync();
}
```

### 4.3 Authority model

| Kind | Who decides | How |
|------|-------------|-----|
| Turn-based (Ludo, Whot, Draughts, Chess) | Server | Client sends intent (`move`, `play_card`). Server engine validates, applies, broadcasts new state. Client never mutates authoritative state. |
| Randomness (dice, shuffles) | Server | Server RNG only. Ludo uses commit-reveal so players can verify dice fairness after the match. |
| Hidden information (Whot hands) | Server | Each player receives a per-seat view. Never send another player's hand to a client. |
| Shot physics (Pool) | Server-verified | Shooter sends shot parameters. Shared deterministic physics (fixed-point, fixed timestep) runs on client for display and in Python on server for the official result. |
| Score-attack (Twin Lanes, Wahala, Downhill, Super Race solo) | Client plays, server checks | Client submits score + seed + input log. Server replays or plausibility-checks before the score hits a leaderboard. Unverified scores never reach ranked boards. |
| Real-time PvP (fighting, live racing) | Later phase | Dedicated game server or rollback netcode. Not built on Django Channels. |

### 4.4 Rules engines: one truth, two languages

Turn-based rules live in `backend/engines/<game>/` as pure Python (no Django, no I/O). This is the canonical implementation.

The Unity client needs the same rules for move highlighting, offline play and AI. That mirror lives in `apps/mobile/Assets/GameVerse/Games/<Game>/Rules/`.

To stop the two drifting apart:
- `shared/rules-fixtures/<game>/*.json` holds golden test vectors: a starting state, a sequence of actions, and the expected resulting states, legal moves and outcome.
- Both the pytest suite and the Unity EditMode suite load the same fixtures. A rules change is not done until both pass.

### 4.5 Realtime protocol

One WebSocket per player session: `/ws/play/<sessionId>/?ticket=...`. Messages are JSON envelopes:
```json
{ "t": "action", "seq": 42, "sessionId": "...", "payload": { ... } }
```
- Server assigns a monotonically increasing `stateVersion`. Clients drop stale updates.
- Reconnect: client sends last known `stateVersion`, server replies with a full snapshot.
- Turn timers run on the server (Celery or Redis key expiry). On timeout: auto-play for Ludo/Whot, flag fall for Chess.
- Disconnect grace window 60s for casual, 30s for ranked, then forfeit.

### 4.6 Matchmaking
- Redis-backed queues keyed by `gameId:mode:ratingBand`.
- Ratings: Glicko-2 per game.
- Private rooms: 6-character room codes (no ambiguous characters like O/0, I/1), share links `gameverse.app/r/ABC123`, best-of-three and rematch built into the room, not the game.

## 5. Repository layout

```
gameverse/
  CLAUDE.md
  README.md
  docker-compose.yml
  .env.example
  apps/
    mobile/              Unity project
    web/                 React app
  backend/               Django project (apps listed in 4.1)
  packages/
    design-tokens/       tokens.json + generator
    api-schema/          exported OpenAPI + generated clients
  shared/
    rules-fixtures/      golden vectors per game
    protocol/            JSON schemas for SessionManifest, envelopes, events
  infrastructure/
    docker/
    deploy/
  docs/
    architecture/        ADRs (docs/architecture/adr-0001-*.md)
    api/
    game-design/
    design/
  .claude/
    agents/
    commands/
```

Record every significant decision as an ADR.

## 6. UI quality bar

Read `docs/design/DESIGN_SYSTEM.md` before writing any UI. The short version:
- Dark-first, premium, calm surfaces, color comes from the games themselves.
- Every interactive element has pressed, hover (web), disabled and loading states.
- Every screen has designed empty, loading (skeletons, not spinners) and error states.
- Motion is purposeful and fast. No animation over 400ms in navigation.
- 60fps target on a mid-range Android (e.g. Tecno, Infinix, Samsung A-series). Test on low-end hardware, because that is the real audience.
- No generic component-library look. If it looks like a default template, it is not done.

## 7. Security baseline
- Server never trusts client game outcomes. See 4.3.
- Rate limit auth, room creation, chat and score submission.
- Room codes and invite links expire.
- WebSocket tickets are single-use and short-lived, not the JWT itself.
- Chat: off by default for under-18 accounts, quick-chat presets for everyone, profanity filter on free text.
- Secrets only through environment variables. Run a secret scan in CI.
- Soft currency is a ledger (append-only transactions), never a mutable balance column alone.

## 8. Out of scope until explicitly approved
- Real money of any kind: entry fees, staking, cash prizes, withdrawals, crypto.
- Paid loot boxes or any randomized reward bought with real money.
- Ads of any kind, including rewarded ads (ADR-0002).
- Microservices.
- iOS native builds (web covers iPhone users first).

The economy and tournament models must still be designed so real-money features could be added later behind a jurisdiction-gated feature flag after legal review.

## 9. Definition of done
- Feature works end to end and you demonstrated it (command + output, or test run).
- Tests added and passing (pytest, Unity EditMode, Vitest as relevant).
- Rules changes pass shared fixtures in both languages.
- UI matches the design system, on a 360x800 phone and a 1440 desktop.
- Docs, `.env.example` and README updated.
- Session summary delivered.

## 10. Commands
Fill this in as tooling lands. Keep it accurate.
```
make up            # start the stack
make test          # all backend tests
make fixtures      # validate rules fixtures in Python
make tokens        # regenerate design tokens
make schema        # export OpenAPI + regenerate clients
```
