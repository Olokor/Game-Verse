# Accounts module

Users, device-bound guest accounts, sessions (JWT access tokens + rotating refresh tokens), and the age rule. Decisions: [ADR-0006](../../../../docs/architecture/adr-0006-accounts-and-auth.md).

Status: increment 1 of Prompt 1.1 (guests, sessions, `GET /accounts/me`). Email upgrade, verification, password reset and deletion follow.

| Endpoint | What it does |
|---|---|
| `POST /auth/guest` | Create a guest; returns tokens and the device secret (once) |
| `POST /auth/guest/login` | Guest signs back in with `guestId` + `deviceSecret` |
| `POST /auth/refresh` | Rotate the refresh token (body, or cookie for web) |
| `POST /auth/logout` | End the session (idempotent) |
| `GET /accounts/me` | The signed-in account (`isMinor`: guests always) |

Public surface for other modules: `IAccountDirectory` (`IsMinorAsync`, `GetSummaryAsync`). `AccountUser` is public only for the shared Identity DbContext; an architecture test stops other modules using it.

Module rules (CLAUDE.md 4.1): expose a small public interface, keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
