# Prompt 1.2: Catalog and sessions

- **Status:** done.
- **Commits:** `f7562bf`, `6f71b5b`, `5dc4420`.
- **ADR:** [ADR-0007](../../architecture/adr-0007-catalog-and-sessions.md)

## What was built
- **Game catalog:** modes, player counts, token accents, and status flags (enabled, coming soon, hidden).
- **Sessions:** seats, an append-only move log (a database trigger blocks UPDATE and DELETE), match results, and the SessionManifest endpoint.
- **Web Home** on the real catalog, with inert "Coming soon" cards.

## Owner decisions
- Disabled games show as "Coming soon".
- The pool game's final name is still open.

## Verification
`make test` green; web screenshots at 360×800 and 1440×900.
