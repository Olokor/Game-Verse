# Matchmaking module

Private rooms and room codes (Prompt 1.4, ADR-0009). Coming in Prompt 1.5: Redis queues keyed by `gameId:mode:ratingBand` and Glicko-2 ratings.

## Private rooms

| Endpoint | Who | What it does |
|---|---|---|
| `POST /rooms` | any player | Open a room `{gameId, playerCount, houseRules, bestOf: 1\|3}`. You host it from seat 0. Rate-limited. |
| `GET /rooms/{code}` | any player | The room. 404 if unknown, 410 if expired or closed. |
| `POST /rooms/{code}/join` | any player | Take the first free seat. Lobby only. Rate-limited. |
| `POST /rooms/{code}/ready` | seated player | `{ready}`. After a game, when everyone is ready the next game (or a new series) starts by itself. |
| `PATCH /rooms/{code}/rules` | host | New house rules, lobby only. Everyone's ready flag resets. |
| `POST /rooms/{code}/leave` | seated player | Leave. A new host is chosen, and an empty room closes. Between games, leaving ends the series. During a game the seat frees when the game ends. |
| `POST /rooms/{code}/start` | host | Start the first game, once every seat is filled and ready. |

### Live lobby
- `POST /realtime/tickets {roomCode}` returns a ticket for `/ws/room/{code}` (members only).
- That socket pushes the room as a `room` envelope on connect and after every change.

### Codes
- 6 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789`: no O/0 and no I/1/L.
- Typed codes are case-insensitive.
- A code is unique among rooms that aren't closed.
- Share link: `GameVerse:Rooms:ShareBaseUrl` + code, e.g. `https://gameverse.app/r/ABC234`.

### Expiry
- A lobby closes 30 minutes after its last change.
- A room between games closes 10 minutes after its last change.
- There's no expiry while a game is being played.
- A daily Hangfire job deletes rooms that closed or expired over a day ago.

### Series
- A game starts an `online_private` session with the room's seats and rules. The room then carries the `sessionId`, and players get the manifest from `/sessions/{id}/manifest`.
- When the game's result is recorded, Sessions raises `IMatchFinishedHandler` and the room scores it:
  - each winning seat (both partners in teams) gets a win;
  - the first to `bestOf / 2 + 1` wins takes the series.
- **Rematch:** everyone readies up again, and the next game or a new series starts by itself.

### Concurrency
Every change touches the room row, so Postgres `xmin` (an optimistic concurrency token) makes racing changes to one room conflict. The loser reloads and retries.

## Module rules

Module rules (CLAUDE.md 4.1):
- Only the public interface is shared. Entities and EF configuration stay `internal`.
- Other modules are reached only through their public interfaces (`ISessionService`, `IProfileService`, Realtime's `IRoomFeed`/`IRoomBroadcaster`) or in-process domain events (`IMatchFinishedHandler`).
- `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
