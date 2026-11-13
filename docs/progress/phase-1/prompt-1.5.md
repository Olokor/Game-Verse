# Prompt 1.5: Matchmaking and ratings

- **Status:** done.
- **Commit:** `c903551`.
- **ADR:** [ADR-0010](../../architecture/adr-0010-matchmaking-and-ratings.md)

## What was built
- **Redis queues** keyed by game, mode and player count, claimed atomically (a Lua script).
- **`MatchmakerService`** ticks on every API instance, under a lock per queue.
- **Ranked band:** ±100 rating points, widening 50 every 5 seconds up to ±500. Anyone matches after 60 seconds.
- **Casual:** first in, first out. AI fills empty seats after 20 seconds.
- **Glicko-2:** matches Glickman's worked example exactly. Ratings update when a ranked game's result is recorded.
- **Endpoints:** `/matchmaking/queue` (POST, GET, DELETE) and `/matchmaking/ratings/{gameId}`.

## Owner decisions
- Quick match is casual (a new mode type, `online_casual`), and a new Ranked mode was added. Both offer 2 and 4 players.
- One Ludo rating; a 4-player finish counts as head-to-heads.

## Verification
- Glicko-2 unit tests.
- Integration tests with simulated players, including a 40-player load test and two API instances matching at the same time.
- A live check: a ranked game moved the ratings to 1662 and 1338, and a lone casual player got AI after 20.5 seconds.

## Open items
- Ranked tiers and seasons come later.
- Whot and pool still list their Quick match as ranked; that gets decided when each game lands.
