# Game-Verse
GameVerse – A cross-platform gaming platform bringing together board, puzzle, racing, fighting, adventure, and arcade games. Play solo, challenge friends, compete online, and climb global leaderboards.

## Status

Phase 0 (foundation). Shared rules and contracts libraries (`shared/`) and the backend skeleton (`backend/`) are in place; design tokens, web and Unity are next. Stack: C# end to end (.NET 10 backend, Unity 6 client, shared `GameVerse.Rules` library), see [ADR-0001](docs/architecture/adr-0001-core-technology-stack.md).

## Build and test

Prerequisites: .NET 10 SDK, Docker Desktop (WSL2 backend), GNU make (works from Git Bash or PowerShell).

```
make build    # build backend + shared
make test     # run all .NET tests (integration tests start Postgres and Redis with Testcontainers, so Docker must be running)
make rules    # build the shared libraries and copy them into the Unity project
make schema   # export the OpenAPI document to packages/api-schema/openapi.json
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
| `api` | ASP.NET Core API (`GameVerse__Role=api`) at `127.0.0.1:8080`: `/health`, `/ws/echo`, `/openapi/v1.json` |
| `worker` | same image as `api` with `GameVerse__Role=worker`: runs the Hangfire job server |

To run the API on the host instead (Scalar UI at `/scalar`, Hangfire dashboard at `/hangfire`, both Development and localhost only), start `postgres` and `redis` with `docker compose up -d postgres redis`, then `dotnet run --project backend/src/GameVerse.Api`.

Configuration lives under the `GameVerse` section of `appsettings.json` and is overridden by environment variables such as `GameVerse__ConnectionStrings__Postgres`. See [.env.example](.env.example).

New migration: `dotnet tool restore`, then `dotnet ef migrations add <Name> --project backend/src/GameVerse.Infrastructure --output-dir Persistence/Migrations`.

See [shared/README.md](shared/README.md) for what the shared libraries contain and the rules for writing code there.

## Project docs

| Doc | What it covers |
|---|---|
| [CLAUDE.md](CLAUDE.md) | How the project is built: stack, architecture, rules, definition of done |
| [docs/PROMPTS.md](docs/PROMPTS.md) | Phase-by-phase build plan |
| [docs/game-design/GAME_BRIEFS.md](docs/game-design/GAME_BRIEFS.md) | Rules, modes and MVP scope for each game |
| [docs/design/DESIGN_SYSTEM.md](docs/design/DESIGN_SYSTEM.md) | Colors, type, motion, components, screens |
| [docs/architecture/](docs/architecture/) | Architecture decision records (ADRs) |
