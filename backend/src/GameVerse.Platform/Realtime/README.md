# Realtime module

Live play over WebSockets (CLAUDE.md 4.5, ADR-0009): tickets, the `/ws/play/{sessionId}` socket, and the match host that runs every server-hosted session: online, and vs AI (ADR-0008).

## Public surface

- `IRealtimeTickets`: issues single-use socket tickets. Sessions puts one in every server-hosted manifest.
- `RealtimeModule.IsHosted(modeKind)`: everything except local pass-and-play and solo.
- **Endpoints:**
  - `POST /realtime/tickets {sessionId}`: a fresh ticket for a seated player, for reconnects. Rate-limited.
  - `/ws/play/{sessionId}?ticket=...`: the game socket.

## How a message travels

```
client -> socket (any instance) -> Redis list rt:match:{id}:inbox (+ poke) -> MatchHost (one instance, holds the lease)
MatchHost -> Postgres move log -> Redis snapshot rt:match:{id}:snap -> pub/sub rt:match:{id}:out -> every instance's sockets
```

- **One authority per match.** `MatchHost` is an actor: a single loop reads a `Channel` mailbox, so actions apply in order with no locks.
  - **Which instance hosts it:** the one holding the lease `rt:match:{id}:lease` (`SET NX`, 10s, renewed every 3s).
  - **Takeover:** connections call "ensure host" on connect and on every heartbeat, so a dead instance's matches move to a live one within the lease time.
- **The move log is the truth.** Each accepted action follows the same path:
  1. it is appended to `sessions.move_logs`;
  2. it is snapshotted to Redis;
  3. each seat gets its own `ViewFor` as a `state` envelope.

  A new host resumes from the snapshot, then replays any logged moves the snapshot missed.
- **Idempotent actions:** an action whose `seq` is not above the last one seen for that seat is answered with a `snapshot`, never applied twice.
- **Timers**, on `TimeProvider` so tests control them:
  - **Turn clock:** 15s per decision online. On timeout the server rolls, or moves with the level-2 AI.
  - **Disconnect grace:** 60s in casual modes, 30s in ranked. After that the server forfeits the seat.
  - **AI pacing.**
  - **vs AI** has no turn clock. It pauses while its player is away, and unloads after 10 minutes.

## Connection limits (CLAUDE.md section 7)

| Limit | Value | When exceeded |
|---|---|---|
| Ticket | single-use, 30 seconds, stored hashed | |
| Message type | text only | the socket closes |
| Message size | 4 KB | the socket closes |
| Message rate | 20 per second (token bucket) | an `error` reply; 5 refusals in a row close the socket |
| Envelope format | must be well formed | a malformed one gets an `error` reply and a log entry; 5 in a row close the socket |
| Heartbeat | the server pings every 15s | no pong for 45s closes the socket |

## Settings

The section is `GameVerse:Realtime` (see `RealtimeOptions`). Every duration is there. `PublicBaseUrl` sets the host used in `wsUrl`.

## Module rules

Module rules (CLAUDE.md 4.1):
- Only the public interface above is shared. Entities and EF configuration stay `internal`.
- Other modules are reached only through their public interfaces (`ISessionService`, `IMatchGames`) or in-process domain events.
- `GameVerse.Architecture.Tests` fails `make test` if this module uses another module's internal types.
