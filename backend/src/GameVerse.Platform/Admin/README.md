# Admin module

/admin-api endpoints for live-ops, audit log of every admin write.

Status: not started. See docs/PROMPTS.md for the prompt that builds it.

Module rules (CLAUDE.md 4.1): expose a small public interface (e.g. `IAdminService`), keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
