# ADR-0007: Game catalog and sessions data model

- **Status:** Accepted (catalog part; the sessions part is added when it lands in Prompt 1.2)
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

## Consequences

- Turning a game on is a data change, not a deploy.
- Clients style games from tokens, so a rebrand of category colors needs no catalog change.
- Open question for the owner: the final name of the pool game.
