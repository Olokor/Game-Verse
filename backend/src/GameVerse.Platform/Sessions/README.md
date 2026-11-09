# Sessions module

Game sessions, match records, append-only move logs, replays, SessionManifest endpoint.

Status: not started. See docs/PROMPTS.md for the prompt that builds it.

Module rules (CLAUDE.md 4.1): expose a small public interface (e.g. `ISessionsService`), keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
