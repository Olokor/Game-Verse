# Sessions module

Game sessions, seats, the append-only move log and match results. Decisions: [ADR-0007](../../../../docs/architecture/adr-0007-catalog-and-sessions.md).

| Endpoint | What it does |
|---|---|
| `POST /sessions` | Start an offline session (pass and play, vs AI, solo); returns the SessionManifest |
| `GET /sessions/{id}/manifest` | The SessionManifest (participants only; others get 404) |
| `GET /sessions/me?status=` | The signed-in player's recent sessions |

Public surface for other modules: `ISessionService` (`CreateAsync`, `AppendMoveAsync`, `RecordResultAsync`, `GetManifestAsync`). `sessions.move_logs` is append-only: a database trigger rejects UPDATE and DELETE. Moves are opaque JSON until each game's engine validates them (Ludo: Prompt 1.3).

Module rules (CLAUDE.md 4.1): expose a small public interface, keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
