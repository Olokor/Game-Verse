# ADR-0001: Core technology stack (C# end to end)

- **Status:** Accepted
- **Date:** 2026-11-09
- **Deciders:** Wisdom (owner), Claude Code (tech lead)
- **Source:** `CLAUDE.md` section 3 ("Locked technical decisions")
- **Supersedes:** the earlier Django/Python ADR-0001 from the same day, which was withdrawn before any application code was written (see "History" below).

## Context

GameVerse is a cross-platform gaming platform: Android first, web second, iOS later. Eleven launch games share one account, one soft-currency wallet, one friends list, matchmaking, leaderboards and rewards. They range from turn-based board and card games to deterministic physics and 3D arcade racing.

Forces that shape the stack:

- **The server is authoritative for every competitive result.** Turn-based rules must run on the server. The client needs the *same* rules for move highlighting, offline play and AI.
- **Several games are verified by replay.** Pool shots, Twin Lanes, Downhill and Wahala submissions are re-simulated on the server. A replay only proves anything if server and client compute exactly the same result.
- **The client is Unity, so the client language is C#.** That is fixed by the choice of engine.
- **Low-end Android is the target device.** It needs 60fps on mid-range hardware (Tecno, Infinix, Samsung A-series) and small downloads.
- **One owner, small team.** Fewer languages and fewer copies of the same logic matter more than framework convenience.

## Decision

### 1. C# end to end, built around two shared libraries

| Library | Contents | Target |
|---|---|---|
| `shared/GameVerse.Rules` | Rules engines, move generation, AI, fixed-point deterministic physics, seeded PRNG, replay verifiers | `netstandard2.1`, C# 9, **zero dependencies** |
| `shared/GameVerse.Contracts` | SessionManifest, realtime envelopes, GameEvents, action/state DTOs | `netstandard2.1`, C# 9, plain POCOs |

The ASP.NET Core server and the Unity client load the **same compiled DLLs**. `make rules` builds them in Release and copies them into `apps/mobile/Assets/GameVerse/Plugins/Shared/`, with a `link.xml` so IL2CPP stripping keeps them intact.

Constraints, enforced in CI:

- `netstandard2.1` and `LangVersion 9.0`, because that is what Unity 6 loads and compiles. No file-scoped namespaces, `record struct`, `required` members, primary constructors on classes or collection expressions.
- `GameVerse.Rules` has no package references: no ASP.NET, Unity, EF Core, logging or `System.Text.Json`.
- **Determinism** in anything replayed or verified: no `float`/`double` in simulation state (use the fixed-point type in `GameVerse.Rules.Math`), no `System.Random` (use the project PRNG), no `DateTime.Now`, no dependence on `Dictionary` iteration order. The server runs on CoreCLR and Android ships IL2CPP, and floating-point results can differ between them.

### 2. Backend: .NET 10 (LTS)

| Concern | Choice |
|---|---|
| Host | ASP.NET Core, Minimal APIs grouped by module |
| Shape | Modular monolith: `GameVerse.Api` (host), `GameVerse.Platform` (modules, internal by default), `GameVerse.Infrastructure` (EF Core, Redis, Hangfire, adapters). Module boundaries checked by NetArchTest. |
| Realtime | Raw ASP.NET Core WebSockets with our own JSON envelope protocol (CLAUDE.md 4.5). One `MatchHost` actor per live match, Redis snapshot after every action. |
| Data | PostgreSQL 16 via EF Core + Npgsql, migrations committed. Redis 7 via StackExchange.Redis. |
| Background work | Hangfire with PostgreSQL storage. Turn timers in-process, with Redis key expiry as the safety net. |
| Auth | ASP.NET Core Identity, JWT access tokens (15 min), rotating refresh tokens stored hashed, guest accounts upgradable to full accounts |
| API contract | `Microsoft.AspNetCore.OpenApi` + Scalar in development. TypeScript client generated with `openapi-typescript` + `openapi-fetch`. Unity uses the Contracts DTOs directly, so no generated C# client. |
| Cross-cutting | Built-in rate limiting, FluentValidation, Serilog (JSON to stdout), OpenTelemetry, Sentry (env-gated) |
| Tests | xUnit, Shouldly, Testcontainers (real Postgres and Redis), `WebApplicationFactory` |
| Quality | Nullable enabled, warnings as errors, .NET analyzers, `dotnet format` in CI |

### 3. Clients

- **Mobile:** Unity 6 LTS, URP, one project for the shell and all games, one assembly and Addressables group per game. Platform UI in UI Toolkit. Unity handles presentation, input and networking only and never reimplements a rule.
- **Web:** React 19, TypeScript, Vite, Tailwind v4, Radix, Motion, TanStack Query, Zustand, React Router. Unity games are embedded with `react-unity-webgl`, one small Web build per game.
- **Admin:** `apps/admin`, a React app on the same stack and tokens, talking to `/admin-api` (admin role required, every write audit-logged). .NET has no built-in admin panel the way Django does, so we build one.
- **Design tokens:** `packages/design-tokens/tokens.json` is the single source, and `build.mjs` generates CSS for web/admin and USS for Unity.

### 4. Infrastructure

Docker Compose locally (`postgres`, `redis`, `api`, `worker`, `web`, `admin`). The `worker` is the API image running in the Hangfire server role. Production runs multi-stage images on `mcr.microsoft.com/dotnet/aspnet:10.0` (chiseled variant), non-root, behind Traefik and Cloudflare, with WebSocket idle timeouts raised at both.

## Why C# end to end

With a Python backend, every rule would exist twice: once in Python on the server and once in C# in Unity. Golden fixtures can catch drift, but they cannot remove it. Pool physics is the worst case. The two implementations would have to agree bit for bit on fixed-point collision results, or server-verified shots would disagree with what the player saw.

With a C# backend:

- **One implementation.** The server validates moves with exactly the code the client uses to highlight and predict them.
- **Replay verification is trustworthy.** Twin Lanes, Wahala, Downhill and Pool are verified by running the same deterministic library that produced the result on the device.
- **Wire types are shared.** Contracts DTOs are the same types on both sides, so there is no generated C# client to keep in step.
- **The later real-time PvP server** (fighting, live racing) can be a .NET service reusing `GameVerse.Rules`.

Fixtures stay, but their role changes. They are now a regression suite and a readable rulebook the owner can review, not a sync mechanism between two languages. When a rule is disputed, the fixture decides.

## Why raw WebSockets instead of SignalR

SignalR is the default realtime choice on ASP.NET Core, but we don't use it for game traffic:

- **Unity Web builds can't use the .NET SignalR client.** WebGL has no raw sockets or threads, so the client has to go through the browser's WebSocket API. A JavaScript SignalR bridge would work on web, but Android would then need a different client and protocol path.
- **`NativeWebSocket` works on both Android and Web.** With a plain WebSocket and our own JSON envelope (`{ t, seq, sessionId, payload }`), every client (Unity Android, Unity Web, the React shell, test harnesses) speaks one protocol.
- **We need explicit control** over `stateVersion` ordering, reconnect-with-snapshot, heartbeat (15s ping, 45s idle close), per-connection size and rate limits, and single-use tickets. SignalR's hub abstraction hides the details we have to own.
- **Scale-out doesn't need SignalR's backplane.** Match state lives in Redis and fan-out uses Redis pub/sub, so no sticky sessions are needed.

We give up SignalR's automatic reconnection, transport fallback and RPC-style hubs. We build reconnection ourselves as part of the protocol. Transport fallback (long polling) isn't needed because every target supports WebSockets.

## Consequences

**Positive**
- One rules implementation, tested once, shipped to server and client as the same binary.
- Deterministic verification for score-attack and physics games is feasible.
- One language across server, client and shared code. Strong typing and analyzers catch whole classes of bugs before runtime.

**Negative / costs**
- **C# 9 ceiling in shared code.** Modern C# features are unavailable in `GameVerse.Rules` and `GameVerse.Contracts`. The server projects can use the latest C#.
- **Two JSON serializers.** The server uses System.Text.Json and Unity uses Newtonsoft. Contracts stay plain POCOs with explicit converters on each side, tested by round-tripping server output in Unity.
- **No free admin panel.** `apps/admin` is real work in Phase 2.
- **We own the realtime protocol:** reconnection, heartbeats and backpressure are our code and need tests.
- **Owner ramp-up.** The owner's strongest languages are Python and Java. C# is close to Java, and .NET patterns without an obvious equivalent are explained as they are introduced.
- **Chess move generation is ours** (bitboards, verified by perft) instead of `python-chess`. Stockfish stays a separate server-side process for strong AI, because of its GPL licence.

## Alternatives considered

| Option | Why not |
|---|---|
| Django + Channels backend, Python engines mirrored in Unity C# (the original ADR-0001) | Every rule written twice. Fixtures reduce drift but don't eliminate it. Deterministic physics parity between Python and IL2CPP is a standing risk. |
| SignalR for game traffic | No .NET SignalR client in Unity Web builds. It would split the protocol by platform. See above. |
| Node/TypeScript backend | Same duplication problem as Python, and it adds a third language. |
| Per-game microservices | Ops cost far beyond a small team. Modular monolith instead, with one planned exception for real-time PvP later. |

## History

The first ADR-0001 (commit `6bb4bc2`) recorded a Django/Python backend. It was withdrawn in commit `595a376`, before any application code existed, when CLAUDE.md was rewritten around the shared C# library. ADR-0002 (dev tooling, written for the Python stack) was withdrawn in the same commit. The ADR-0002 number is now reserved for the web-delivery decision from Prompt 0.7.
