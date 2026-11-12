# ADR-0010: Matchmaking queues and Glicko-2 ratings

- **Status:** Accepted
- **Date:** 2026-11-12
- **Deciders:** Wisdom (owner: casual and ranked queues; 4-player games rated), Claude Code (tech lead: queue design, bands, timings)

## Context

Prompt 1.5 asks for:
- Redis matchmaking queues for Ludo 2p and 4p, run as a hosted service;
- Glicko-2 ratings tested against reference values;
- a rating band that widens over time;
- AI filling seats after a timeout, **only in casual mode**.

Ludo had a single public mode, "Quick match", and it was ranked.

## Decisions: product (owner, 2026-11-12)

### Two public queues for Ludo

| Mode | Kind | Rated | AI fill | Reconnect window | Players |
|---|---|---|---|---|---|
| Quick match | `online_casual` (new) | no | yes, after a wait | 60 seconds | 2 or 4 |
| Ranked | `online_ranked` | yes | never | 30 seconds | 2 or 4 |

- **`online_casual` joins the shared mode vocabulary** (CLAUDE.md 4.2, `ModeKinds` and `GameModes`).
- **Existing databases:** the catalog seeder never updates rows, so migration `AddRatingsAndCasualQueue` switches Ludo's Quick match to the new kind once. The seeder adds the Ranked row.
- **Other games' queues** are decided when each game becomes playable.

### One rating per game
- **2p and 4p ranked games both move a player's Ludo rating.**
- **4p games count as head-to-heads by finishing order:** a better place is a win against that player, a worse place a loss, the same place a draw.
- **Leavers already finish last** (ADR-0008), so leaving counts as a loss against everyone still in the game.

## Decisions: engineering (tech lead; values may be revisited)

### Queues in Redis
- **A queue** is a sorted set `mm:q:{game}:{mode}:{players}` of user ids, scored by join time.
- **Each waiting player** has an entry `mm:e:{user}` holding the queue, rating and join time.
  - The entry expires after an hour, so a crash mid-join can never lock someone out.
  - One search per player at a time: `SET NX`, and a second join gets 409.
- **A finished search** is written to `mm:m:{user}` (session id, kept 10 minutes) for the player's next poll.
- **Why not one key per rating band:** CLAUDE.md 4.6 sketched queues keyed `gameId:mode:ratingBand`. A band that **widens per player** can't be a fixed key, because a player would have to hop between keys as their wait grows. Queues are therefore keyed by game, mode and player count, and each player's band is computed from their stored rating and their wait.

### The matcher
- **`MatchmakerService`** is a hosted service on every API instance, ticking every second.
- **One instance works a queue per tick**, under a Redis lock `mm:lock:{queue}`.
- **A group is claimed by a Lua script that removes all its players only if every one is still waiting.** A cancel or a second matcher can never split a game or put a player in two (an integration test runs two instances at once).

**Ranked:**
- **Band:** ±100 rating points when the search starts, then +50 every 5 seconds of waiting, up to ±500.
- **After 60 seconds, any rating is accepted.** A small player pool must still get games; Glicko-2 absorbs mismatches through the expected score.
- **Grouping:** the longest waiter anchors a game. It takes the closest players whose own bands, and the anchor's band, both cover the gap, until the game is full.

**Casual:**
- **First in, first out; ratings are ignored.** It's unrated, so the fastest game wins.
- **AI fill:** once the longest waiter has waited **20 seconds**, their game starts with whoever is there plus level-2 AI seats. A casual game always has at least one person.

**Every game formed:**
- is created through `ISessionService.CreateAsync` with seats in random order, Classic Naija rules, and its own dice commitment (ADR-0008);
- is then played on the realtime match host exactly like a room game (ADR-0009);
- if session creation fails, its players return to the queue with their places kept.

### Status for clients
- Clients poll `GET /matchmaking/queue` while searching. It returns `searching` (with the wait and the current band) or `matched` (with the session id).
- A push channel can come with the Unity matchmaking screen if polling proves too slow.

### Glicko-2
- **Exactly Glickman's 2013 description:** τ = 0.5, the Illinois iteration for volatility, and new players at 1500 / 350 / 0.06.
- **The unit test reproduces his worked example:** 1464.06 / 151.52 / 0.05999.
- **A rating period is one game.** Everyone in it is rated from everyone's ratings before it, at the same time.
- **Idle drift:** the deviation widens once per whole day without a rated game (step 6), capped at 350.
- **Provisional:** a rating is shown as provisional while its deviation is above 110.
- **Storage:** `matchmaking.ratings`, one row per player and game, with Postgres `xmin` as the concurrency token: two results for one player at once retry instead of losing an update.
  - Doubles are fine because this is server-only state that is never replayed. The determinism rules apply to `GameVerse.Rules`.
- **Updates happen in `RatingsOnFinish`**, an `IMatchFinishedHandler`. It rates `online_ranked` sessions whose seats are all people.

### Settings (`GameVerse:Matchmaking`)

| Setting | Default |
|---|---|
| `BandStart` | 100 |
| `BandStep` / `BandStepSeconds` | 50 / 5 |
| `BandMax` | 500 |
| `AnyoneAfterSeconds` | 60 |
| `AiFillSeconds` | 20 |
| `AiLevel` | 2 |
| `TickMilliseconds` | 1000 |
| `JoinsPerMinute` | 30 per IP |
| `MatchedMinutes` | 10 |

## Consequences

- **Matchmaking is a stateless tier over Redis:** any number of API instances can run it.
- **Ratings move only on server-decided results.** A client can't report a ranked win.
- **Rewards:** casual games can earn rewards later (Progression, Prompt 2.1) without touching ratings.
- **Season resets and ranked tiers** (GAME_BRIEFS section 13) build on `matchmaking.ratings` later.
- **Test hosts park the background matcher** (they share one Redis). Matchmaking tests drive ticks themselves, plus one test with the loop running.
