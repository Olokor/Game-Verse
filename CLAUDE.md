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

You are the senior engineer and tech lead. The owner (Wisdom) reviews and decides product questions. His strongest languages are Python and Java, and he is comfortable with Linux, Docker and Traefik. C# is the project language, so when you introduce a .NET pattern that has no obvious Java or Python equivalent, explain it in one or two lines.

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

### Why C# end to end
Unity is C#. With a C# backend, every game's rules, AI helpers and deterministic physics live in **one shared library** used by both the server and the Unity client. The server validates with exactly the code the client predicts with. There is no second implementation to keep in sync. This is the main reason for the stack; protect it.

### Shared libraries (the heart of the project)
- `shared/GameVerse.Rules` holds pure game logic: rules engines, move generation, AI, deterministic physics, seeded RNG, replay verifiers.
- `shared/GameVerse.Contracts` holds wire types: SessionManifest, realtime envelopes, GameEvents, action and state DTOs.
- Both target **`netstandard2.1`** so Unity 6 can load them.
- Both are capped at **C# 9** (`<LangVersion>9.0</LangVersion>`), because that is what Unity 6 compiles. No file-scoped namespaces, no `record struct`, no `required` members, no primary constructors on classes, no collection expressions. CI fails if the lang version is raised.
- **No dependencies** in `GameVerse.Rules`: no ASP.NET, no Unity, no EF Core, no logging framework, no `System.Text.Json`. Pure logic and plain data in, plain data out. `GameVerse.Contracts` may carry serializer attributes only if both Newtonsoft (Unity) and System.Text.Json (server) are handled, so prefer plain POCOs with explicit converters on each side.
- **Determinism rules** for anything replayed or verified (Pool physics, Twin Lanes, Downhill, Wahala verification): no `float`/`double` in simulation state, use the fixed-point type in `GameVerse.Rules.Math`. No `Dictionary` iteration order dependence, no `DateTime.Now`, no `System.Random` (use the project's seeded PRNG). The server runs on CoreCLR and Unity Android ships IL2CPP, so floating-point results can differ between them.
- Delivery to Unity: `make rules` builds both libraries in Release and copies the DLLs and XML docs into `apps/mobile/Assets/GameVerse/Plugins/Shared/`, plus the PRNG and contracts golden fixtures into `Assets/GameVerse/Resources/Fixtures/` (Unity tests and the on-device self-check read the same files as `dotnet test`). Add a `link.xml` so IL2CPP stripping keeps them intact. The copied DLLs are build output; never edit them in Unity.
- Fixed-point type: **Q32.32 stored in a `long`** (32 integer bits, 32 fraction bits). Multiplication and division go through 128-bit intermediates so they never overflow silently. Enough range and precision for Pool, Downhill and Twin Lanes. Change only with an ADR, because it changes every replay.
- No C# `record` types or `init` setters in shared code (they need an `IsExternalInit` shim on netstandard2.1 and are fragile across the Unity DLL boundary). Use classes with constructors and get-only properties in Rules, plain `get; set;` POCOs in Contracts. See `shared/README.md`.
- One solution: `backend/GameVerse.slnx` includes the shared projects, so a single `dotnet build` / `dotnet test` covers backend and shared code. Shared projects still never reference backend projects.

### Backend (C# / .NET)
- **.NET 10 (LTS)**, ASP.NET Core, Minimal APIs grouped by module.
- **Realtime: raw ASP.NET Core WebSockets** with our own JSON envelope protocol (section 4.5), not SignalR for game traffic. Reason: Unity Web builds cannot use the .NET SignalR client, while `NativeWebSocket` works on Android and Web alike. One protocol for every client.
- PostgreSQL 16 via **EF Core** + Npgsql. Migrations committed. Redis 7 via `StackExchange.Redis`.
- Background and scheduled work (tournaments, daily quest rotation, season resets, turn timers backup): **Hangfire** with PostgreSQL storage. Turn timers themselves use in-process timers plus Redis key expiry as the safety net.
- Auth: ASP.NET Core Identity for users, JWT bearer access tokens (15 min) and rotating refresh tokens stored hashed. Guest accounts allowed and upgradable to full accounts.
- OpenAPI via `Microsoft.AspNetCore.OpenApi`, browsed with Scalar in development. TypeScript client generated with `openapi-typescript` + `openapi-fetch`. Unity uses the DTOs from `GameVerse.Contracts` directly, so no generated C# client is needed.
- Rate limiting: built-in `Microsoft.AspNetCore.RateLimiting`.
- Validation: FluentValidation on request models.
- Logging and tracing: Serilog (JSON to stdout), OpenTelemetry. Error reporting: Sentry SDKs (API, web, Unity) sending to self-hosted GlitchTip, off unless a DSN is set, no PII; see ADR-0005.
- Tests: **xUnit v3** on Microsoft.Testing.Platform (opted in via `global.json`; coverage via `dotnet test --coverage`), **Shouldly** for assertions, **Testcontainers** for real Postgres and Redis in integration tests, `WebApplicationFactory` for API tests. Rules library needs near-total branch coverage.
- Code quality: `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, .NET analyzers on, `dotnet format` in CI. Module boundaries enforced with NetArchTest tests.

### Admin console
.NET has no built-in admin panel, so we build our own. `apps/admin` is a small React app (same stack and tokens as web) for support and live-ops: find player, view match history and move logs, ban/mute, grant or reverse currency (through the ledger, with a reason), edit quests and tournaments, feature flags. Admin endpoints live under `/admin-api`, require the `admin` role, and every write is audit-logged.

### Mobile (Unity)
- Unity 6, C#, URP. 2D Renderer for 2D games, Forward+ for 3D games. Pinned to 6000.6.8f1 (6.6, a Supported release) until Unity 6.7 LTS ships, then upgrade; see ADR-0004.
- Project settings and generated assets come from `Assets/GameVerse/Editor/ProjectSetup.cs` (`make unity-setup`), never from editor clicks. All JSON goes through `GameVerse.Net.GameVerseJson.Settings`, which matches the server's System.Text.Json output (checked against `shared/rules-fixtures/contracts`).
- One Unity project. The platform shell and every game live in it. Games are separate assemblies (`.asmdef`) and Addressables groups so they can be downloaded on demand.
- Game rules come from the shared `GameVerse.Rules` DLL. Unity code handles presentation, input and networking only. Never reimplement a rule inside Unity.
- Platform UI is built with **UI Toolkit** (UXML + USS), not uGUI. These are text files, which keeps them reviewable and generatable. Use uGUI only inside a game where world-space UI demands it.
- Animation: DOTween or LitMotion for code tweens, **Rive** for designed motion (shared with web).
- Networking: REST via a thin `UnityWebRequest` wrapper using Contracts DTOs, realtime via `NativeWebSocket`, async via UniTask. JSON via `com.unity.nuget.newtonsoft-json`.
- Input System package, Unity Localization package, Addressables, Cinemachine for 3D games.
- Tests: Unity Test Framework (EditMode for presentation logic, PlayMode for flows). Rules are tested in the .NET test project, not in Unity. Run headless with `-batchmode -runTests`.
- Scenes stay minimal. Prefer prefabs and code-driven setup, created through editor scripts where possible, so changes are diffable.

### Web
- React 19 + TypeScript + Vite
- Tailwind CSS v4, Radix UI primitives, Motion (Framer Motion) for transitions, Rive web runtime
- TanStack Query for server state, Zustand for client state, React Router
- Unity games embedded with `react-unity-webgl`. Each game ships its own small Web build, loaded only when played.
- Tests: Vitest + Testing Library, Playwright for end-to-end flows.

### Shared design tokens
`packages/design-tokens/tokens.json` is the single source for color, type, spacing, radius, motion. A Node script (`packages/design-tokens/build.mjs`) generates:
- `apps/web/src/styles/tokens.css` (CSS variables consumed by Tailwind)
- `apps/admin/src/styles/tokens.css`
- `apps/mobile/Assets/GameVerse/UI/Tokens.uss` (USS variables, for every token USS can express)
- `apps/web/src/styles/theme.css` and `apps/admin/src/styles/theme.css` (Tailwind v4 `@theme` mapping utilities onto the variables; it resets Tailwind's default palette, spacing and radius so only token values exist, e.g. `bg-bg-surface`, `p-16`, `rounded-md`)
- `apps/mobile/Assets/GameVerse/UI/Tokens.g.cs` (`GameVerse.UI.GameVerseTokens` C# constants, every token: easings, springs, fonts and durations for code tweens)

`make tokens-check` (part of `make test`) fails if any output is stale or missing a token. Variables are named `--gv-<path>`, e.g. `--gv-color-brand-volt`. See `packages/design-tokens/README.md`.

Never hardcode a color, font size, radius or duration in components. If a token is missing, add it to `tokens.json`.

### Infrastructure
- Dev machine: Windows 11 + Docker Desktop (WSL2 backend). `make` targets must work from Git Bash and PowerShell.
- Tooling: .NET 10 SDK, Node LTS with a `pnpm` workspace for `apps/web`, `apps/admin` and `packages/*`, GitHub Actions for CI, Git LFS for binary assets, LF line endings via `.gitattributes`.
- Docker Compose for local dev: `postgres`, `redis`, `api` (ASP.NET Core), `worker` (same image, Hangfire server role), `web`, `admin`.
- Images: multi-stage builds, runtime on `mcr.microsoft.com/dotnet/aspnet:10.0` (chiseled variant in production), non-root user.
- Production: containers behind Traefik with Cloudflare in front. Sticky sessions are not required because realtime state lives in Redis, but WebSocket idle timeouts must be raised at both Traefik and Cloudflare. Environment-based config via `appsettings.json` + environment variables (`GameVerse__Section__Key`); `.env` never committed, `.env.example` always current.
- Modular monolith. No microservices. The one planned exception is a dedicated real-time game server for twitch-action PvP (fighting, live racing), which is a later phase and needs approval. Being on .NET keeps that option open, since the same shared rules library can run inside it.

## 4. Architecture

### 4.1 Backend solution layout
```
Directory.Build.props          (repo root) nullable, warnings as errors, analyzers; applies to backend/ and shared/
Directory.Packages.props       (repo root) central package versions for backend/ and shared/
global.json                    SDK version, opts dotnet test into Microsoft.Testing.Platform
backend/
  GameVerse.slnx               solution file (.NET 10 XML format), includes shared/ projects
  src/
    GameVerse.Api/             host: Program.cs, endpoint wiring, WebSocket endpoint, auth, DI
    GameVerse.Platform/        modules, one folder each, internal by default:
      Accounts/                users, guest accounts, auth tokens
      Profiles/                avatars, frames, titles, stats
      Catalog/                 game registry: id, category, modes, versions, feature flags
      Sessions/                game sessions, match records, move logs, replays
      Matchmaking/             queues, rating bands, private rooms, room codes
      Realtime/                connection manager, protocol, presence, game hosts
      Social/                  friends, invites, clubs
      Progression/             XP, levels, mastery, achievements, quests, season pass
      Economy/                 soft currency ledger, cosmetics inventory, store catalog
      Tournaments/             brackets, schedules, prizes (non-monetary for MVP)
      Leaderboards/            Redis sorted sets + periodic Postgres snapshots
      Notifications/           in-app + push (FCM)
      Moderation/              reports, chat filter, bans
      Admin/                   admin-api endpoints, audit log
    GameVerse.Infrastructure/  EF Core DbContext, migrations, Redis, Hangfire, FCM, Stockfish adapter
  tests/
    GameVerse.Platform.Tests/
    GameVerse.Api.IntegrationTests/
    GameVerse.Architecture.Tests/   NetArchTest module boundary rules
shared/
  Directory.Build.props/.targets  netstandard2.1 + C# 9 + zero-dependency build guard
  GameVerse.Rules/              netstandard2.1, C# 9, zero dependencies
  GameVerse.Contracts/          netstandard2.1, C# 9
  GameVerse.Rules.Tests/        net10.0, xUnit, loads golden fixtures
  rules-fixtures/<game>/*.json  golden vectors
```

Module rules: a module exposes a small public interface (e.g. `IProgressionService`) and keeps its entities and EF configuration internal. Modules talk through those interfaces or in-process domain events, never by reading another module's tables.

### 4.2 Game Integration Contract

A game never handles auth, matchmaking, rewards or leaderboards itself. It receives a **SessionManifest** and emits **GameEvents**. Both are defined once in `GameVerse.Contracts`.

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

Every game implements `IGameModule` in Unity:
```csharp
public interface IGameModule {
    string GameId { get; }
    UniTask LoadAsync(SessionManifest manifest, IGameHost host);
    void ApplyServerState(GameStateEnvelope envelope);
    UniTask UnloadAsync();
}
```

Every turn-based game implements `IGameEngine` in `GameVerse.Rules`, used identically by server and client:
```csharp
public interface IGameEngine<TState, TAction, TConfig> {
    TState NewGame(TConfig config, IReadOnlyList<SeatInfo> seats, ulong seed);
    IReadOnlyList<TAction> LegalActions(TState state, int seat);
    ApplyResult<TState> Apply(TState state, int seat, TAction action);
    GameOutcome? Outcome(TState state);
    TState ViewFor(TState state, int seat);   // hides other seats' private info
}
```
State is immutable from the caller's view: `Apply` returns a new state or a rejection reason.

### 4.3 Authority model

| Kind | Who decides | How |
|------|-------------|-----|
| Turn-based (Ludo, Whot, Draughts, Chess) | Server | Client sends intent (`move`, `play_card`). Server runs the shared engine, applies, broadcasts new state. Client uses the same engine only for highlighting legal moves and offline play. |
| Randomness (dice, shuffles) | Server | Server seeds the shared PRNG. Ludo uses commit-reveal so players can verify dice fairness after the match. |
| Hidden information (Whot hands) | Server | Each player receives `ViewFor(seat)`. Never send another player's hand to a client. |
| Shot physics (Pool) | Server-verified | Shooter sends shot parameters. The shared fixed-point simulation runs on the client for display and on the server for the official result. Same code, same result. |
| Score-attack (Twin Lanes, Wahala, Downhill, Super Race solo) | Client plays, server checks | Client submits score + seed + input log. Server replays with the shared verifier or plausibility-checks before the score hits a leaderboard. Unverified scores never reach ranked boards. |
| Real-time PvP (fighting, live racing) | Later phase | Dedicated .NET game server or rollback netcode, reusing `GameVerse.Rules`. |

### 4.4 Rules engines: one truth, one language

All rules live in `shared/GameVerse.Rules/<Game>/`. The server and Unity load the same compiled library.

Golden fixtures still matter, now as a regression and documentation tool:
- `shared/rules-fixtures/<game>/*.json` holds starting state, a sequence of actions, and the expected states, legal moves and outcome.
- `GameVerse.Rules.Tests` runs every fixture. A rules change is not done until they pass.
- Fixtures are readable by the owner, so they double as the agreed rulebook. When a rule is disputed, the fixture decides.
- Chess also uses standard perft counts to verify move generation.

### 4.5 Realtime protocol

One WebSocket per player session: `/ws/play/{sessionId}?ticket=...`. Messages are JSON envelopes defined in `GameVerse.Contracts`:
```json
{ "t": "action", "seq": 42, "sessionId": "...", "payload": { ... } }
```
- Server assigns a monotonically increasing `stateVersion`. Clients drop stale updates.
- Each live match is owned by one `MatchHost` actor (a single-reader `System.Threading.Channels` queue) so actions are applied strictly in order with no locks around game state.
- Match state is snapshotted to Redis after every applied action, so a restarted or different API instance can resume the match. Redis pub/sub fans out messages when more than one API instance runs.
- Reconnect: client sends last known `stateVersion`, server replies with a full snapshot.
- Turn timers run on the server. On timeout: auto-play for Ludo/Whot, flag fall for Chess.
- Disconnect grace window 60s for casual, 30s for ranked, then forfeit.
- Heartbeat ping every 15s; idle connections are closed after 45s without a pong.

### 4.6 Matchmaking
- Redis-backed queues keyed by `gameId:mode:ratingBand`, processed by a hosted background service.
- Ratings: Glicko-2 per game, implemented in `GameVerse.Platform/Matchmaking` with tests against published reference values.
- Private rooms: 6-character room codes (no ambiguous characters like O/0, I/1), share links `gameverse.app/r/ABC123`, best-of-three and rematch built into the room, not the game.

## 5. Repository layout

```
gameverse/
  CLAUDE.md
  README.md
  Makefile
  docker-compose.yml
  .env.example
  apps/
    mobile/              Unity project
    web/                 React app (players)
    admin/               React app (live-ops)
  backend/               .NET solution (section 4.1)
  shared/                GameVerse.Rules, GameVerse.Contracts, fixtures (section 4.1)
  packages/
    design-tokens/       tokens.json + build.mjs
    api-schema/          exported OpenAPI + generated TS client
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
- WebSocket tickets are single-use and short-lived (30s), not the JWT itself.
- Validate WebSocket message size and rate per connection; drop and log malformed envelopes.
- Chat: off by default for under-18 accounts, quick-chat presets for everyone, profanity filter on free text.
- Secrets only through environment variables or a secret store. Run a secret scan in CI.
- Soft currency is a ledger (append-only transactions), never a mutable balance column alone. Ledger writes are idempotent by request key.
- Admin actions require the admin role and are audit-logged with actor, target, reason and timestamp.

## 8. Out of scope until explicitly approved
- Real money of any kind: entry fees, staking, cash prizes, withdrawals, crypto.
- Paid loot boxes or any randomized reward bought with real money.
- Microservices.
- iOS native builds (web covers iPhone users first).

Ads are in scope. Ad placements follow the player-respect rules in `GAME_BRIEFS.md` section 13: no ads after death, no ads between turns in multiplayer, no ads that delay play. Rewarded ads are opt-in. The ad network and SDK need an ADR and owner approval before integration.

The economy and tournament models must still be designed so real-money features could be added later behind a jurisdiction-gated feature flag after legal review.

## 9. Definition of done
- Feature works end to end and you demonstrated it (command + output, or test run).
- Tests added and passing (`dotnet test`, Unity tests where relevant, Vitest/Playwright for web).
- Rules changes pass all golden fixtures.
- Shared libraries still build as `netstandard2.1` with C# 9 and the Unity copy is refreshed (`make rules`).
- UI matches the design system, on a 360x800 phone and a 1440 desktop.
- Docs, `.env.example` and README updated.
- Session summary delivered.

## 10. Commands
Fill this in as tooling lands. Keep it accurate.
```
make build         # dotnet build backend/GameVerse.slnx (backend + shared)
make test          # token check, token tests, web typecheck + Vitest, dotnet test for backend + shared
make rules         # build GameVerse.Rules + Contracts (Release) and copy DLLs into Unity
make up            # start postgres, redis, migrate, api, worker (docker compose, needs .env); waits until healthy
make down          # stop the stack (volumes kept)
make logs          # follow api + worker logs
make migrate       # apply EF Core migrations (one-shot container)
make schema        # export OpenAPI to packages/api-schema/openapi.json and generate schema.d.ts
make install       # pnpm install for the workspace (apps/web, apps/admin, packages/*)
make web           # web dev server, http://localhost:5173 (component gallery at /dev/components)
make web-test      # web typecheck + Vitest (includes the no-hardcoded-design-values scan)
make e2e           # Playwright smoke + screenshots at 360x800 and 1440x900
make unity-setup   # apply Unity project settings and generated assets (ProjectSetup.Apply)
make unity-test    # Unity EditMode tests headless (UNITY=path overrides the editor)
make android       # IL2CPP ARM64 development APK -> apps/mobile/Build/Android/GameVerse.apk
make device-check  # install on the USB phone, launch, require "GV_SELFCHECK prng ok" and GV_ERRORCHECK (event found in GlitchTip when sent)
make web-spike     # Prompt 0.7 spike (throwaway): release Web builds of the 2D/3D test scenes -> apps/web/public/unity, prints sizes
# Linux/CI Unity tests: unity-editor -batchmode -nographics -projectPath apps/mobile -runTests -testPlatform EditMode -testResults TestResults/editmode.xml -logFile -
make tokens        # regenerate design tokens (CSS, USS, C#) from packages/design-tokens/tokens.json
make tokens-check  # fail if a token output is stale or missing a token
make errors-up     # start self-hosted GlitchTip (error reporting, ADR-0005) on 127.0.0.1:8000
make errors-setup  # create the GlitchTip admin, org and api/web/unity projects; print the DSNs for .env
make errors-down   # stop GlitchTip (data kept)
```