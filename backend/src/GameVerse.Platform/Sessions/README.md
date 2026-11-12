# Sessions module

Game sessions, seats, the append-only move log and match results. Decisions: [ADR-0007](../../../../docs/architecture/adr-0007-catalog-and-sessions.md).

| Endpoint | What it does |
|---|---|
| `POST /sessions` | Start an offline session (pass and play, vs AI, solo); returns the SessionManifest |
| `GET /sessions/{id}/manifest` | The SessionManifest (participants only; others get 404) |
| `GET /sessions/{id}/fairness` | Dice commit-reveal: the commitment always, the secret and seed once the session has ended (participants only) |
| `GET /sessions/me?status=` | The signed-in player's recent sessions |

Public surface for other modules: `ISessionService` (`CreateAsync`, `AppendMoveAsync`, `RecordResultAsync`, `GetManifestAsync`, `GetSeedAsync`, `GetFairnessAsync`). `sessions.move_logs` is append-only: a database trigger rejects UPDATE and DELETE. Moves are opaque JSON here; the Realtime match host validates them with the shared engine before appending (Prompt 1.4).

## Per-game setup (`GameSetups.cs`)

- **House rules** arrive as strings in `config.houseRules`.
  - Ludo's keys are in `GameVerse.Contracts.Ludo.LudoHouseRules`.
  - An unknown key, a bad value, or teams without 4 players gets a 400 that names the problem.
  - A key that's left out takes its Classic Naija default (ADR-0008).
- **Commit-reveal (ADR-0008).**
  - For games with server randomness (Ludo today), creation stores 32 random bytes in `seed_secret` and their SHA-256 in `seed_commitment`.
  - The engine seed is `SeedCommitment.DeriveSeed(secret)`.
  - The secret leaves the server only through `/fairness`, and only after the session has ended.
- **Initial state.** The manifest's `initialState` is a `LudoStateDto`: the local seat's view of `NewGame`, without the dice PRNG.

Module rules (CLAUDE.md 4.1): expose a small public interface, keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
