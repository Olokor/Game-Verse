# ADR-0008: Ludo rules, board model and dice fairness

- **Status:** Accepted
- **Date:** 2026-11-12
- **Deciders:** Wisdom (owner: default house rules, game end, teams), Claude Code (tech lead: board model, engine shape, commit-reveal)

## Context

Ludo is the first real game (Prompt 1.3). Its rules live once, in `shared/GameVerse.Rules/Ludo`, and run on the server (authoritative) and in Unity (highlighting, offline play, AI). Ludo is played many ways in Nigeria, so the defaults and the end-of-game rule were product decisions. The fixtures in `shared/rules-fixtures/ludo/` are the agreed rulebook (CLAUDE.md 4.4).

## Decisions: rules (owner)

### Default house rules: "Classic Naija"

| Rule | Default | Kind |
|---|---|---|
| Six to leave base | on | toggle |
| Three sixes in a row forfeit the turn (the third six isn't moved) | on | toggle |
| A capture gives another roll | on | toggle |
| Safe squares (the 4 starts and 4 stars) | on | toggle |
| Must capture when possible | off | toggle |
| Quick mode (each player starts with one token out) | off | toggle |
| A six gives another roll | always | fixed |
| An exact roll is needed to reach home | always | fixed |
| Reaching home gives another roll | always | fixed |

There are **no blocks**: two tokens on one square don't form a wall, and landing there captures every opponent token on it.

### Game end: the players choose

- `placings` is 1, 2 or 3: the game ends once that many players have finished.
- It's capped at players minus 1, so the last player never has to play alone.
- The players who haven't finished rank by tokens home, then total progress, then seat number (a deterministic tie-break).
- **Default 1** (first home wins), which keeps quick matches short. Rooms choose.
- A player who finishes drops out of the turn order.
- The manifest shows the effective value (`LudoEngine.EffectivePlacings`), not the value that was asked for.

### Teams 2v2

- 4 players only. Partners sit opposite: seats 0 and 2, and seats 1 and 3.
- Partners never capture each other and may share squares.
- A player whose 4 tokens are home keeps rolling and moves the partner's tokens.
- The team wins when all 8 of its tokens are home.

## Decisions: engine (tech lead)

### Board model
- **Token position, relative to its own colour's path:**
  - `-1`: in base;
  - `0..50`: the shared track;
  - `51..55`: the token's home column;
  - `56`: home.
- **Absolute track square** = `(13 × colour + position) mod 52`, for positions 0 to 50.
- **Safe squares** are absolute 0, 13, 26 and 39 (the starts) and 8, 21, 34 and 47 (the stars).
- **Colours:** two players take opposite corners (colours 0 and 2), three take 0, 1 and 2, and four take all four.

### Leaving and forfeit (tech lead; owner may revisit)

CLAUDE.md 4.5 forfeits a player after the disconnect grace window. The engine models this as one action, `{"forfeit": true}`.

- **Who sends it:**
  - a player resigning, at any time and even out of turn;
  - the server, after a timeout or a disconnect.
  - It is never listed in `LegalActions`.
- **What happens:**
  - The leaver is skipped from then on.
  - The leaver's tokens stay on the board but are inert: they can't be captured and they threaten nobody.
  - The game ends when only one player is still in it.
  - Reason `opponents_left`.
- **Ranking:**
  - Leavers rank last.
  - Among leavers, whoever left later ranks higher.
- **Teams:** a partner who leaves forfeits for the whole team.
  - This is the simplest fair rule. The alternative, where the partner plays on alone, is a product choice for later.
- **Rejections:** a forfeit from a seat that has already left or finished is rejected with `not_playing`.

### Turn flow
- The phases are `roll`, `move` and `over`.
- **There is no pass action.** A roll with no legal move ends the turn by itself (or rolls again on a six), so a client can never stall a game.
- **Rejection codes:** `game_over`, `not_your_turn`, `roll_first`, `must_move`, `illegal_move`, `not_playing`, and `dice_hidden` (a player's view has no dice).
- **A finished team player's move:** `{"move": n}` refers to the partner's token `n`.

### AI
- **Placement:** `LudoAi` lives in the shared library, so the server's AI seats and offline Unity play behave the same.
- **Determinism:** the AI is deterministic. It uses integer scores only, and ties go to the lowest token.
- **Level 1** picks a random legal move from a PRNG the caller passes in. That is never the dice PRNG.
- **Level 2** uses integer weights. Strongest first:
  1. capture;
  2. reach home;
  3. leave base;
  4. enter the home column;
  5. escape danger;
  6. land on a safe square;
  7. progress.
- **Level 3** is expectimax over the next roll.
  - It counts a third six as a forfeit.
  - It scores a finished game by place: any place above last beats an unfinished position (this matters with placings 2 and 3).
- **Measured over 100 seeded 2-player games:**

  | Match | Strong side's wins |
  |---|---|
  | L3 vs L1 | 95 |
  | L2 vs L1 | 89 |
  | L3 vs L2 | 59 |

### Dice fairness: commit-reveal
1. **At creation**, the server makes 32 random bytes (`RandomNumberGenerator`) and stores them in `sessions.game_sessions.seed_secret`, which is server-only.
   - It publishes `seed_commitment = SHA-256(secret)` as hex.
   - The dice seed is `SeedCommitment.DeriveSeed(secret)`: the first 8 bytes of SHA-256("gameverse-seed-v1" || secret), little-endian.
2. **During the match**, dice come from the xoshiro256** state inside `LudoState`.
   - `ViewFor` strips that state. It is the only hidden information in Ludo, because it would predict every future roll.
   - Clients get `LudoStateDto`, which has no PRNG field.
3. **After the match**, `GET /sessions/{id}/fairness` returns the secret and the seed to participants.
   - Anyone can check `SHA-256(secret) == commitment` and replay every roll with `GameVerse.Rules`.
   - Before the end, it returns only the commitment.

## Consequences

- **Fixtures are the rulebook.** Changing a rule means changing a fixture in `shared/rules-fixtures/ludo/`. A disputed rule is settled by the fixture.
- **Replays break if the dice order changes.** The dice sequence depends on the PRNG and on one `NextInt(1, 7)` per roll. Changing either breaks old replays, which needs a new ADR, just like the fixed-point type.
- **Scripted dice are test-only.** The test-only scripted-dice constructor is `internal`, so production code can't bypass the PRNG.
- **States are tamper-proof.** State lists are read-only wrappers, and the `LudoState` constructor validates its input, because states are also restored from snapshots (Redis in Prompt 1.4).
- **House rules are strict.** They arrive as strings in `SessionConfig.houseRules`, with keys in `GameVerse.Contracts.Ludo.LudoHouseRules`. Unknown keys and bad values are rejected with a 400.
