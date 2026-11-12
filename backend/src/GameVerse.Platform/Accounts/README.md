# Accounts module

Users, device-bound guest accounts, sessions (JWT access tokens + rotating refresh tokens), and the age rule. Decisions: [ADR-0006](../../../../docs/architecture/adr-0006-accounts-and-auth.md).

Status: Prompt 1.1 complete (guests, sessions, email accounts, deletion).

| Endpoint | What it does |
|---|---|
| `POST /auth/guest` | Create a guest; returns tokens and the device secret (once) |
| `POST /auth/guest/login` | Guest signs back in with `guestId` + `deviceSecret` |
| `POST /auth/refresh` | Rotate the refresh token (body, or cookie for web) |
| `POST /auth/logout` | End the session (idempotent) |
| `POST /auth/upgrade` | Signed-in guest becomes an email account (same id); mails a 6-digit code |
| `POST /auth/email/verify` / `resend` | Confirm the email with the code / mail a new one |
| `POST /auth/login` | Email + password (5 failures lock for 15 minutes) |
| `POST /auth/password/forgot` / `reset` | Mail a reset code (always 202) / set a new password, revoke every session |
| `GET /accounts/me` | The signed-in account (`isMinor`: guests always) |
| `POST /accounts/me/delete` | Delete and anonymise the account (email accounts confirm with their password) |

Public surface for other modules: `IAccountDirectory` (`IsMinorAsync`, `GetSummaryAsync`). `AccountUser` is public only for the shared Identity DbContext; an architecture test stops other modules using it.

Module rules (CLAUDE.md 4.1): expose a small public interface, keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
