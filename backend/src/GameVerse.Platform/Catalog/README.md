# Catalog module

The game registry: every game's title, tagline, category, accent (a design-token path), version, status and modes. Decisions: [ADR-0007](../../../../docs/architecture/adr-0007-catalog-and-sessions.md).

| Endpoint | What it does |
|---|---|
| `GET /catalog/games` | All listed games in build order with their modes (public, cached 60 s) |
| `GET /catalog/games/{id}` | One game (hidden or unknown: 404) |

Status is the per-game feature flag: `enabled` (playable), `coming_soon` (listed, not playable) or `hidden`. The 11 launch games are seeded with the migrations (`CatalogSeed.cs`, insert-if-missing); only Ludo is enabled. Public surface for other modules: `ICatalog.GetGameAsync`.

Module rules (CLAUDE.md 4.1): expose a small public interface, keep entities and EF configuration `internal`, talk to other modules only through their public interfaces or in-process domain events. `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
