# Profiles module

Profile basics: display name and avatar (Prompt 1.1). Frames, titles and stats arrive with Progression. Decisions: [ADR-0006](../../../../docs/architecture/adr-0006-accounts-and-auth.md).

| Endpoint | What it does |
|---|---|
| `GET /profiles/me` | The signed-in player's profile |
| `PATCH /profiles/me` | Change `displayName` (3 to 20 letters, digits, spaces, underscores) and/or `avatarId` (`ProfileAvatars.All`) |

Public surface for other modules: `IProfileService` (`CreateDefaultAsync`, `GetAsync`, `AnonymiseAsync`) and `DisplayNameRule`. Accounts creates the default profile for every new guest and anonymises it on deletion. The `profiles.profiles` table has no foreign key into another module's tables.

TODO(gameverse): profanity and impersonation filter for display names (Moderation module).

Module rules (CLAUDE.md 4.1): expose a small public interface, keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
