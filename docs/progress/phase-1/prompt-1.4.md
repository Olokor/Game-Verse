# Prompt 1.4: Realtime play and private rooms

- **Status:** done.
- **Commit:** `b326b0f`.
- **ADR:** [ADR-0009](../../architecture/adr-0009-realtime-and-rooms.md)

## What was built
- **Tickets:** single-use, 30 seconds, stored hashed.
- **`/ws/play/{sessionId}`.**
- **The match host:** an actor per match, with:
  - a Redis inbox, a Redis lease, and a pub/sub fan-out;
  - the Postgres move log, then a Redis snapshot, after every action;
  - resume by replaying the move log.
- **Timers:**
  - a 15-second turn clock with auto-play;
  - a disconnect grace of 60 seconds (casual) or 30 seconds (ranked), then a forfeit;
  - a heartbeat (ping every 15s, closed after 45s without a pong);
  - a Hangfire watchdog.
- **Private rooms:**
  - codes without look-alike characters, and share links;
  - seats, ready states, host-only rules and Start;
  - a lobby socket (`/ws/room/{code}`);
  - best of 3, and a rematch by everyone readying up.

## Decisions (tech lead; the owner may revisit)
- A 15-second turn clock. Only first place counts in a series.
- A lobby expires after 30 idle minutes, and a room 10 minutes after its last game.
- A second socket for the lobby.

## Verification
- 16 realtime tests and 6 room tests.
- A live check that killed the API container mid-game and resumed at the same move.

## Open items
- **Deploy:** set `GAMEVERSE_REALTIME_PUBLIC_BASE_URL`, and raise the WebSocket idle timeouts in Traefik and Cloudflare.
- Kicking a player, spectators and room chat are not built yet.
