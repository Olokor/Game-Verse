# Game-Verse
GameVerse – A cross-platform gaming platform bringing together board, puzzle, racing, fighting, adventure, and arcade games. Play solo, challenge friends, compete online, and climb global leaderboards.

## Status

Phase 1 in progress (Ludo vertical slice): accounts and auth (Prompt 1.1, [ADR-0006](docs/architecture/adr-0006-accounts-and-auth.md)) and the game catalog and sessions (Prompt 1.2, [ADR-0007](docs/architecture/adr-0007-catalog-and-sessions.md)) are done. Phase 0 (foundation) is complete: shared rules and contracts libraries (`shared/`), the backend skeleton (`backend/`), design tokens (`packages/design-tokens/`), the web shell (`apps/web/`) and the Unity project (`apps/mobile/`) are in place. Stack: C# end to end (.NET 10 backend, Unity 6 client, shared `GameVerse.Rules` library), see [ADR-0001](docs/architecture/adr-0001-core-technology-stack.md).

## Build and test

Prerequisites: .NET 10 SDK, Node 22+ with pnpm 12 (`npm install -g --allow-scripts=pnpm pnpm@12`), Docker Desktop (WSL2 backend), GNU make (works from Git Bash or PowerShell).

```
make build    # build backend + shared
make test     # token check, token tests and all .NET tests (integration tests start Postgres and Redis with Testcontainers, so Docker must be running)
make rules    # build the shared libraries and copy them into the Unity project
make schema   # export the OpenAPI document to packages/api-schema/openapi.json
make tokens   # regenerate design tokens (CSS, USS, C#) from packages/design-tokens/tokens.json
```

## Web app

```
make install   # once: pnpm install for the workspace
make web       # dev server at http://localhost:5173 ; component gallery at /dev/components
make web-test  # typecheck + Vitest
make e2e       # Playwright smoke + screenshots at 360x800 and 1440x900 (first run: pnpm --filter @gameverse/web exec playwright install chromium)
```

React 19, TypeScript, Vite, Tailwind v4 on the generated design tokens, Radix, Motion, TanStack Query, Zustand and React Router. Home and `/games` read the game catalog from the API (`GET /catalog/games` through the typed client in `src/lib/api.ts`; run `make up` first, or set `VITE_API_URL`). Quests, friends and Continue playing are still mock until their prompts; every mock is marked `TODO(gameverse)`. Tests stub the API with `src/test/catalogFixture.ts`.

## Unity app

Unity 6000.6.8f1 with Android Build Support (see [apps/mobile/README.md](apps/mobile/README.md) and [ADR-0004](docs/architecture/adr-0004-unity-editor-version.md)).

```
make unity-test     # EditMode tests headless (PRNG parity with dotnet test, wire format round trip)
make android        # IL2CPP ARM64 development APK
make device-check   # install on a USB-connected phone and print the on-device rules self-check
```

## Run the local stack

```
cp .env.example .env    # once
make up                 # builds the API image, starts postgres, redis, api, worker; migrations run first
curl http://localhost:8080/health
make logs               # follow api + worker logs
make down               # stop (data volumes are kept)
make migrate            # apply EF Core migrations on demand
```

| Service | What it is |
|---|---|
| `postgres` | PostgreSQL 16, `127.0.0.1:5432` |
| `redis` | Redis 7, `127.0.0.1:6379` |
| `migrate` | one-shot container from the API image that applies migrations and exits |
| `api` | ASP.NET Core API (`GameVerse__Role=api`) at `127.0.0.1:8080`: `/health`, `/auth/*`, `/accounts/me`, `/profiles/me` (ADR-0006), `/ws/echo`, `/openapi/v1.json` |
| `worker` | same image as `api` with `GameVerse__Role=worker`: runs the Hangfire job server |
| `mailpit` | catches every email the API sends: inbox at http://127.0.0.1:8025 (codes for verification and password reset). Set `GAMEVERSE_EMAIL_*` in `.env` to send real mail instead |

To run the API on the host instead (Scalar UI at `/scalar`, Hangfire dashboard at `/hangfire`, both Development and localhost only), start `postgres` and `redis` with `docker compose up -d postgres redis`, then `dotnet run --project backend/src/GameVerse.Api`.

Configuration lives under the `GameVerse` section of `appsettings.json` and is overridden by environment variables such as `GameVerse__ConnectionStrings__Postgres`. See [.env.example](.env.example).

New migration: `dotnet tool restore`, then `dotnet ef migrations add <Name> --project backend/src/GameVerse.Infrastructure --output-dir Persistence/Migrations`.

See [shared/README.md](shared/README.md) for what the shared libraries contain and the rules for writing code there.

## Error reporting

Errors from the API, the web app and Unity go to a self-hosted [GlitchTip](https://glitchtip.com) through the official Sentry SDKs ([ADR-0005](docs/architecture/adr-0005-error-reporting-glitchtip.md)). Reporting is off unless a DSN is set, and events carry no personal data (no IPs, headers, cookies, query strings or names).

```
make errors-up      # GlitchTip at http://127.0.0.1:8000 (compose profile "errors")
make errors-setup   # admin user, "gameverse" org, api/web/unity projects; prints DSNs + an API token for .env
```

Then set `GAMEVERSE_ERROR_DSN_API`, `VITE_ERROR_DSN`, `UNITY_ERROR_DSN` and `GLITCHTIP_API_TOKEN` in `.env`. The web app reads the repo-root `.env` (`envDir`). Open `/dev/crash` in a dev build to see the error screen and send a test error. Log in with `GLITCHTIP_ADMIN_EMAIL` / `GLITCHTIP_ADMIN_PASSWORD`.

**Manual fallback, if `errors-setup` fails after a GlitchTip upgrade:**
1. Run `docker compose exec glitchtip ./manage.py createsuperuser`.
2. Log in at http://127.0.0.1:8000.
3. Create the organization `gameverse` and the projects `api`, `web` and `unity`.
4. Copy each project's DSN from Settings → Client Keys. For the API container, swap the host for `glitchtip:8000`.

To check the API against a running GlitchTip: set `GAMEVERSE_E2E_GLITCHTIP_DSN` (api DSN with `127.0.0.1:8000`) and `GLITCHTIP_API_TOKEN`, then run `dotnet test --project backend/tests/GameVerse.Api.IntegrationTests`. Without them that test is skipped.

## Project docs

| Doc | What it covers |
|---|---|
| [CLAUDE.md](CLAUDE.md) | How the project is built: stack, architecture, rules, definition of done |
| [docs/PROMPTS.md](docs/PROMPTS.md) | Phase-by-phase build plan |
| [docs/game-design/GAME_BRIEFS.md](docs/game-design/GAME_BRIEFS.md) | Rules, modes and MVP scope for each game |
| [docs/design/DESIGN_SYSTEM.md](docs/design/DESIGN_SYSTEM.md) | Colors, type, motion, components, screens |
| [docs/architecture/](docs/architecture/) | Architecture decision records (ADRs) |
