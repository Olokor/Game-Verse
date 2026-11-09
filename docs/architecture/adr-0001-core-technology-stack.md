# ADR-0001: Core technology stack

- **Status:** Accepted
- **Date:** 2026-11-09
- **Deciders:** Moses (owner), Claude Code (tech lead)
- **Source:** `CLAUDE.md` section 3 ("Locked technical decisions")

## Context

GameVerse is a cross-platform gaming platform: Android first, web second, iOS later. Eleven launch games share one account, one soft-currency wallet, friends, matchmaking, leaderboards and rewards. The launch games range from turn-based board and card games to physics and 3D arcade games.

Constraints that shape the stack:

- **One owner and a small team.** The owner writes Python first and Java second, and is comfortable with Linux, Docker and Traefik. The stack should favour one backend language and one game engine.
- **Low-end Android is the target device.** It needs 60fps on mid-range hardware (Tecno, Infinix, Samsung A-series) and small downloads.
- **The server is authoritative for every competitive result.** Turn-based rules must run on the server. The client also needs the same rules for move highlighting, offline play and AI.
- **The same games run on mobile and web,** under one design language.
- **Many games, one shell.** Games must plug into a shared platform without each one re-implementing auth, matchmaking or rewards.

## Decision

### Backend (Python)

| Concern | Choice |
|---|---|
| Framework | Django 5.x, Django REST Framework |
| Realtime | Django Channels (WebSockets) with `channels-redis` |
| Data | PostgreSQL 16, Redis 7 |
| Background and scheduled work | Celery with a Redis broker (worker + beat) |
| Auth | JWT via `djangorestframework-simplejwt`, 15-minute access tokens, rotating refresh tokens. Guest accounts, upgradable to full accounts. |
| API contract | `drf-spectacular` OpenAPI schema. Clients are generated, never hand-written: TypeScript via `openapi-typescript` + `openapi-fetch`; C# via NSwag or a small generator script, committed to the Unity project. |
| Tests | `pytest`, `pytest-django`, `factory_boy`. Rules engines need near-total branch coverage. |
| Quality | `ruff` (lint + format), `mypy` on rules engines and services |
| Observability | Structured JSON logging. Sentry, enabled by environment variable. |
| Shape | Modular monolith (Django apps per domain, see `CLAUDE.md` 4.1). Rules engines in `backend/engines/` are pure Python with no Django imports and no I/O. |

### Mobile (Unity)

| Concern | Choice |
|---|---|
| Engine | Unity 6 LTS, C#, URP. 2D Renderer for 2D games, Forward+ for 3D games. |
| Project structure | One Unity project at `apps/mobile` holding the platform shell and every game. Each game is its own assembly (`.asmdef`) and Addressables group, so it can be downloaded on demand. |
| Platform UI | UI Toolkit (UXML + USS). uGUI only inside a game where world-space UI requires it. |
| Animation | DOTween or LitMotion for code tweens. Rive for designed motion, shared with web. |
| Networking | REST via the generated C# client. Realtime via `NativeWebSocket`. |
| Packages | Input System, Localization, Addressables, Cinemachine (3D games) |
| Tests | Unity Test Framework: EditMode for rules and logic, PlayMode for flows, run headless with `-batchmode -runTests`. |
| Scenes | Kept minimal. Setup is prefab- and code-driven, created by editor scripts where possible, so changes can be diffed. |

### Web

| Concern | Choice |
|---|---|
| App | React 19, TypeScript, Vite |
| Styling and UI | Tailwind CSS v4 (on generated token CSS variables), Radix UI primitives, Motion for transitions, Rive web runtime |
| State | TanStack Query (server state), Zustand (client state), React Router |
| Games | Unity Web builds embedded with `react-unity-webgl`. One small build per game, loaded only when played. |
| Tests | Vitest + Testing Library. Playwright for end-to-end flows. |

### Shared design tokens

`packages/design-tokens/tokens.json` is the single source for color, type, spacing, radius and motion. A build script generates `apps/web/src/styles/tokens.css` and `apps/mobile/Assets/GameVerse/UI/Tokens.uss`. Components never hardcode these values.

### Infrastructure

- **Local development:** Docker Compose with `postgres`, `redis`, `api` (Django ASGI), `worker` (Celery), `beat` and `web`.
- **Production:** containers behind Traefik, with Cloudflare in front.
- **Configuration:** from environment variables via `.env` files. These are never committed, and `.env.example` is kept current.
- **No microservices.** The one planned exception is a dedicated real-time game server for twitch-action PvP (fighting, live racing). It belongs to a later phase and needs separate approval and its own ADR.

## Alternatives considered

These alternatives were considered before the decisions were locked in `CLAUDE.md`. Each is recorded here so the reasoning isn't lost.

- **Node or Go backend.** Rejected. Python matches the owner's main language, and Django ships auth, admin, ORM and migrations. Pure-Python rules engines also fit naturally next to the API. Django Channels realtime is enough for turn-based play, which is the only realtime need until the later PvP phase.
- **Godot, or native Android plus a separate web engine.** Rejected. Unity covers 2D, physics and 3D games in one engine, exports to both Android and web, and has mature Addressables for on-demand game downloads.
- **uGUI for the platform shell.** Rejected. UI Toolkit's UXML/USS are text files, so they can be reviewed in diffs, generated, and styled from the shared tokens.
- **Microservices.** Rejected for the team size. A modular monolith keeps one deploy and one database, with domain boundaries enforced by Django apps.
- **Hand-written API clients.** Rejected. Generated clients keep web and Unity in lockstep with the server schema.

## Consequences

**Positive**

- One backend language, one game engine and one design-token source, which suits a small team.
- Rules engines are pure Python, so they are fast to test. Shared JSON fixtures in `shared/rules-fixtures/` keep the C# mirror honest (`CLAUDE.md` 4.4).
- Generated clients and tokens mean contract and style drift show up as build or test failures.

**Negative and risks**

- **Every turn-based game's rules live in two languages** (Python canonical, C# mirror). Fixture discipline is mandatory. A rules change isn't done until both suites pass.
- **Unity Web builds are heavy, especially on iOS Safari and low-end Android.** Which game types ship on web is decided by the Phase 0 performance spike (ADR-0002).
- **Django Channels isn't suited to twitch-action real-time play.** That needs a separate server later, as planned.
- **Pool needs deterministic fixed-point physics written twice** (C# and Python). This is the highest technical risk, and is scheduled to be built and tested before any visuals.

## Sub-choices left open

These were left open in `CLAUDE.md` and will be settled in later ADRs, or in the session that first needs them:

1. Code-tween library: DOTween or LitMotion.
2. C# client generator: NSwag or a custom script.
3. ASGI server: Daphne or Uvicorn (`docs/PROMPTS.md` Prompt 0.2 assumes Uvicorn).
4. Async library for `IGameModule` (`CLAUDE.md` 4.2 uses `UniTask`, which isn't yet listed as a dependency).
5. Exact Unity 6 LTS editor version to pin.

## Changing this decision

Any change to the items above needs explicit owner approval and a new ADR that supersedes the relevant part of this one.
