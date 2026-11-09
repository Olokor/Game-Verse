# Game-Verse
GameVerse – A cross-platform gaming platform bringing together board, puzzle, racing, fighting, adventure, and arcade games. Play solo, challenge friends, compete online, and climb global leaderboards.

## Status

Phase 0 (foundation). Shared rules and contracts libraries are in place (`shared/`); backend, web and Unity are next. Stack: C# end to end (.NET 10 backend, Unity 6 client, shared `GameVerse.Rules` library), see [ADR-0001](docs/architecture/adr-0001-core-technology-stack.md).

## Build and test

Prerequisites: .NET 10 SDK, GNU make (works from Git Bash or PowerShell).

```
make test     # run all .NET tests
make rules    # build the shared libraries and copy them into the Unity project
```

See [shared/README.md](shared/README.md) for what the shared libraries contain and the rules for writing code there.

## Project docs

| Doc | What it covers |
|---|---|
| [CLAUDE.md](CLAUDE.md) | How the project is built: stack, architecture, rules, definition of done |
| [docs/PROMPTS.md](docs/PROMPTS.md) | Phase-by-phase build plan |
| [docs/game-design/GAME_BRIEFS.md](docs/game-design/GAME_BRIEFS.md) | Rules, modes and MVP scope for each game |
| [docs/design/DESIGN_SYSTEM.md](docs/design/DESIGN_SYSTEM.md) | Colors, type, motion, components, screens |
| [docs/architecture/](docs/architecture/) | Architecture decision records (ADRs) |
