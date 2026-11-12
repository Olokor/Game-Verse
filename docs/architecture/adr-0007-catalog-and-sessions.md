# ADR-0007: Game catalog and sessions data model

- **Status:** Accepted
- **Date:** 2026-11-12
- **Deciders:** Wisdom (owner: "Coming soon" for disabled games), Claude Code (tech lead: data model)

## Context

Every game shares one registry (the catalog), one way to describe a play session, and one record of moves and results. These are data models shared across all games (CLAUDE.md section 2, rule 6), so the decisions are recorded here.

## Decisions: catalog

### A game row
- `id`: a slug that is also the SessionManifest `gameId`.
- `title`, `tagline`, `category`.
- `accentToken`: a design-token path such as `color.category.board`, never a color value. The web and Unity resolve it with their own generated tokens, so the catalog never bypasses the design system.
- `version` and `sortOrder`.
- `status`: `enabled`, `coming_soon` or `hidden`, the per-game feature flag.
  - **Owner decision:** disabled games are listed as "Coming soon", so the platform shows its breadth from day one.
  - The admin console (Prompt 2.4) flips status.

### Modes
- A mode has a `key` unique within its game, a `kind`, a `label` and its allowed `playerCounts`.
- The kind uses the SessionManifest `mode` vocabulary: `local`, `vs_ai`, `online_ranked`, `online_private`, `solo`.
- Player counts are a list, not a min/max, because Ludo quick match allows 2 or 4 but not 3.
- Modes come from `GAME_BRIEFS.md`.

### Seeding
- The 11 launch games are seeded **with the migrations** (the `migrate` command, `make migrate`, the test fixture) using `INSERT ... ON CONFLICT DO NOTHING`.
- Seeding never updates a row, so admin edits survive deploys. It's safe to run repeatedly or concurrently.

### Reading the catalog
- `GET /catalog/games` and `GET /catalog/games/{id}` are public, with `Cache-Control: public, max-age=60` (rarely changing data, slow networks).
- Hidden games are invisible (404).
- Other modules use `ICatalog`.

### Naming
"8 Ball Pool" is a third-party trademark (CLAUDE.md section 1). The pool game is seeded as **"Pool"** (`pool`) until the owner chooses its name; a test fails if the catalog ever contains "8 Ball".

## Decisions: sessions

### The session row
- A `GameSession` records:
  - game id and version;
  - mode key (catalog) and mode kind (manifest vocabulary);
  - status: `created`, `active`, `finished` or `abandoned`;
  - config as jsonb (variant, house rules, AI level);
  - creator and timestamps;
  - a nullable `SeedCommitment` for Ludo's commit-reveal dice (Prompt 1.3).
- Offline sessions start `active`. Online ones start `created` until a room or match fills.

### Seats
- A `SessionPlayer` is one seat. Humans carry their account id; AI and extra pass-and-play players carry none.
- The display name is a **snapshot** taken at creation, so match history reads correctly after a rename or an account deletion.
- There's no foreign key into `accounts`. Modules don't couple through each other's tables.

### The move log is append-only, enforced by the database
- `MoveLog (session, seq)` stores each accepted action as jsonb with its state version.
- `seq` must be exactly the next number; duplicates and gaps are refused, and the primary key settles races.
- A **Postgres trigger rejects every `UPDATE` and `DELETE`** on `sessions.move_logs`. Replays, disputes and score verification (CLAUDE.md 4.3) can trust the log even against an application bug or a manual query.
- Actions are opaque JSON in Prompt 1.2. Each game's engine validates them before they're appended (Ludo: 1.3).

### Results
- `MatchResult` holds one row per session: outcome (`win`, `draw` or `abandoned`), winning seats, reason, and placements as jsonb. Writing it finishes the session.
- A second result is refused, and no move can be appended after the finish.

### Manifest
- `GET /sessions/{id}/manifest` returns the Contracts `SessionManifest`. Its `mode` is the kind, and players come from the seat snapshots.
- Only participants can read it; anyone else gets the same 404 as a missing session.
- `cosmetics` is empty until Economy (2.2), `initialState` until the engine (1.3), and `realtime` until private rooms (1.4).

### Creating sessions
- `POST /sessions` creates **offline sessions only** (`local`, `vs_ai`, `solo`) for an enabled game and an allowed player count. The caller takes seat 0.
- Online sessions are created through `ISessionService` by private rooms and matchmaking.

## Consequences

- Turning a game on is a data change, not a deploy.
- The move log can grow large: partitioning or archiving finished sessions' moves is a later operational task, not a schema change.
- Clients style games from tokens, so a rebrand of category colors needs no catalog change.
- Open question for the owner: the final name of the pool game.
