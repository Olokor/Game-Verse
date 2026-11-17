# ADR-0009: Realtime play and private rooms

- **Status:** Accepted
- **Date:** 2026-11-12
- **Deciders:** Claude Code (tech lead), Wisdom (owner: approved the Prompt 1.4 plan, including the decisions marked "owner may revisit")

## Context

Prompt 1.4 builds the realtime protocol of CLAUDE.md 4.5 and the private rooms of 4.6.

Constraints:
- The server is the only authority for turn-based games.
- There is one WebSocket protocol for Unity Android and Web, so it uses raw WebSockets, not SignalR (ADR-0001).
- A match must survive its API instance dying.
- More than one API instance may run behind Traefik **without sticky sessions**.

ADR-0008 already puts vs-AI Ludo on the server.

## Decisions: realtime

### Tickets
- **Issued by** `POST /realtime/tickets` and inside every server-hosted manifest.
- **Form:** 32 random bytes (base64url), valid 30 seconds, single-use.
  - Stored in Redis under the ticket's SHA-256.
  - Redeemed with `GETDEL`.
- **A ticket names its target** (a session, or a room's lobby) and the user.
- **The socket handshake redeems the ticket before anything else.** A wrong or used ticket gets 401; a user who isn't seated gets 403.
- **The JWT never goes in a URL.**

### One socket per player session: `/ws/play/{sessionId}?ticket=`
- **Envelopes:** the existing `GameVerse.Contracts.Realtime` envelopes.
- **Client to server:** `action` (with a positive `seq`), `resync` and `pong`.
- **Server to client:**

  | Message | When |
  |---|---|
  | `snapshot` | on connect and resync |
  | `state` | after every accepted action, the seat's own `ViewFor` with `stateVersion`, `appliedSeq`, `turnSeat`, `turnDeadline` |
  | `presence` | a seat connected or left, with `graceEndsAt` |
  | `ended` | the match is over (`MatchEndedEvent`) |
  | `error` | an action or message was refused |
  | `ping` | every 15 seconds |

- **Limits** (CLAUDE.md section 7):
  - text only, at most 4 KB per message;
  - a 20-per-second token bucket per connection;
  - **malformed envelopes** get an `error`, are logged, and close the socket after 5 in a row;
  - **heartbeat:** no pong for 45 seconds closes the socket.

### The match host
- **One authority per match.** `MatchHost` is an actor: a single loop reads its `Channel` mailbox and owns the state, so actions apply in order without locks.
- **Mailbox: a Redis list** (`rt:match:{id}:inbox`) plus a pub/sub poke, not plain pub/sub.
  - The connection and the host may be on different instances.
  - Intents sent while a host is moving between instances wait in the list instead of being lost.
- **Output:** a pub/sub channel that every instance's sockets subscribe to. Each socket forwards only what is addressed to it: everyone, its seat, or its own connection.
  - The same path runs with one instance or many, so tests exercise the multi-instance path every time.
- **Ownership:** a Redis lease (`SET NX`, 10 seconds), renewed every 3 seconds with a compare-and-extend script.
  - Connections call "ensure host" on connect and on every heartbeat.
  - A crashed instance's matches move to a live one within the lease time.
  - A host that fails to renew, or finds its move-log seq taken, stops at once (it lost authority).
- **Every accepted action, in order:**
  1. append to the Postgres move log;
  2. snapshot to Redis (full state with the dice PRNG, version, client seqs, connections, timers);
  3. broadcast.

  The **move log is the truth**: a resuming host loads the snapshot, then replays any newer logged moves through the engine.
- **Idempotent actions:** a client `seq` not above the last one seen for that seat is answered with a snapshot, never applied twice. Resending after a reconnect is safe.
- **Failures stay contained:** a message that fails is logged and answered with `server_error`. It never ends the match.

### Timers (owner may revisit the values)

| Timer | Value | What happens |
|---|---|---|
| Turn clock, online | **15 seconds per decision** | the server plays for the seat: a roll, or the level-2 AI's move |
| Turn clock, vs AI | none | |
| Disconnect grace, private room (casual) | **60 seconds** (CLAUDE.md 4.5) | the server applies `forfeit`; the reason is `opponents_left` once one side remains |
| Disconnect grace, ranked | **30 seconds** (CLAUDE.md 4.5) | the same |
| A new online match | everyone gets the same grace to connect | |
| vs AI | pauses while its player is away, unloads after 10 minutes; the snapshot is kept 24 hours | |

- **Clock source:** game timers run on the injected `TimeProvider` (tests use a fake clock). Leases and ticket expiry are wall clock, because Redis TTLs are.

### The safety net
- **The gap it closes:** a match whose players have all left and whose host instance died would never time out.
- **The job:** `MatchWatchdog` runs every minute on the worker (Hangfire, as CLAUDE.md section 3 plans for turn-timer backup).
  - It reads `rt:deadlines`, a Redis sorted set of each match's next deadline.
  - It starts any overdue match that has no lease.
- **Result:** forfeits and the end still happen with no client connected.

## Decisions: private rooms

### Where rooms live
- **Module:** Matchmaking (CLAUDE.md 4.1).
- **Tables:** `matchmaking.rooms` and `matchmaking.room_seats`.
- **Concurrency:** every change touches the room row, so Postgres `xmin` makes racing changes conflict and retry.

### Codes and links
- 6 characters from a 31-character alphabet with no O/0 and no I/1/L.
- Case-insensitive.
- Unique among rooms that aren't closed.
- Share links: `https://gameverse.app/r/{CODE}` (configurable).

### Flow
1. The host creates the room and its rules (validated like a session's).
2. Players join the first free seat by code.
3. Everyone marks ready, and the host starts.
4. A start creates an `online_private` session with the room's seats. Players load it through its manifest and play on `/ws/play`.

- **Rule changes:** the host may change rules in the lobby, which resets everyone's ready.

### Who may do what

| Who | Can |
|---|---|
| The host | change rules (lobby only) and start the first game |
| Seated players | ready, leave, and follow the lobby socket |
| Anyone signed in | read a room by code and join |

### Series and rematch (owner may revisit)
- **Best of 1 or 3.** A game's winners (both partners in teams) score a win. Placings beyond first don't count.
- **Next game:** after a game, the next one starts **by itself once every seated player is ready**. No host action is needed, and nobody is pulled into a game while away.
- **Rematch:** after a decided series, the same ready-up starts a new series with the score reset.
- **Leaving between games** ends the series: the score resets, the seat opens, and the room returns to the lobby.
- **Leaving during a game:** the seat frees when that game ends. The game itself handles the leaver through resign or the disconnect forfeit.
- **Host:** the role passes to the lowest occupied seat.
- **Empty room:** closes.
- **Scoring is decoupled from the game.** The room scores through `IMatchFinishedHandler`, an in-process event Sessions raises after recording a result. A failing handler is logged and never undoes the result.

### Expiry
| Room state | Closes after |
|---|---|
| Lobby | 30 minutes without a change |
| Between games | 10 minutes |
| A game in play | never |

- Expired codes return 410.
- A daily Hangfire job deletes rooms over a day past closing.

### Live lobby
- `/ws/room/{code}` is a second socket endpoint beside CLAUDE.md 4.5's `/ws/play`. It uses the same envelopes, tickets and heartbeat.
- Server to client only: it pushes the room as a `room` envelope after every change, fanned out through Redis pub/sub.
- Changes go through REST, which keeps validation and rate limits in one place.

### The echo endpoint
`/ws/echo` is now mapped in Development only. Games use `/ws/play`.

## Consequences

- **Scale-out works without sticky sessions.** Any API instance can serve any player of any match. Cost: an extra Redis round trip per message, even on one instance.
- **Recovery holds:**
  - an instance can be killed mid-match;
  - integration tests prove a resume on another instance;
  - the final result replays exactly from the move log and the revealed seed.
- **The protocol grew additively.** New fields:
  - `StateUpdate.turnSeat` and `turnDeadline`;
  - the `presence` and `ended` messages;
  - `TicketRequest`.

  The golden files in `shared/rules-fixtures/contracts/` and Unity's wire-format test cover them.
- **Two Redis keys per live match** (snapshot and inbox) plus a lease. Snapshots expire 24 hours after the last change, or 1 hour after the end.
- **Matchmaking (Prompt 1.5)** creates `online_ranked` sessions the same way rooms create private ones. The match host already applies the ranked 30-second grace.
- **Not yet:**
  - kicking a player from a room;
  - spectators;
  - room chat (Moderation).
