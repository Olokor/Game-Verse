# Leaderboards module

Redis sorted sets, periodic Postgres snapshots; global, friends, country; daily/weekly/season.

Status: not started. See docs/PROMPTS.md for the prompt that builds it.

Module rules (CLAUDE.md 4.1): expose a small public interface (e.g. `ILeaderboardsService`), keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
