# Rules fixtures

Golden test vectors, one folder per game. They do two jobs:
- **A regression suite.** `GameVerse.Rules.Tests` replays every file, and a rules change isn't done until they all pass.
- **The agreed rulebook.** When a rule is disputed, the fixture decides (CLAUDE.md 4.4). Write them so the owner can read them.

## Folders

| Folder | Contents |
|---|---|
| `_selftest/` | Fixtures for a tiny test-only Nim engine. They prove the runner itself works. Not a real game. |
| `prng/` | `xoshiro256ss.json`: PRNG reference outputs. Unity's EditMode test asserts the same values on device. |
| `<game>/` | One folder per game, e.g. `ludo/`. The folder name must equal the fixture's `"game"` field. |

## Fixture format

```jsonc
{
  "game": "ludo",                       // must match the folder name
  "name": "Capture sends the token home",
  "description": "Optional: what rule this pins down and why.",
  "config": { /* the game's config, e.g. house rules */ },
  "seats": [ { "seat": 0, "playerId": "p0", "isAI": false }, { "seat": 1, "playerId": "p1" } ],
  "seed": 42,                            // PRNG seed passed to NewGame
  "initialState": { /* optional: start mid-game instead of from NewGame */ },
  "steps": [
    {
      "seat": 0,
      "action": { /* the game's action */ },
      "expect": {
        "ok": true,                      // default true; false means the engine must reject it
        "error": "not_your_turn",        // required error code when ok is false
        "legalActions": [ /* exact list for this seat, checked BEFORE the action */ ],
        "state": { /* partial state checked AFTER the action: only listed fields are compared */ }
      }
    }
  ],
  "outcome": { "kind": "win", "winningSeats": [0], "placements": [0, 2, 1], "reason": "all_tokens_home" }
  // kind: "win" | "draw" | "abandoned" | "none" (the game must still be running)
}
```

Rules of thumb:
- **One rule per file**, named after the rule (`capture-sends-token-home.json`, `three-sixes-forfeit.json`).
- **Assert only what the rule is about.** `state` is a partial match, so leave out unrelated fields and fixtures survive refactors.
- **Pin every house-rule toggle in both positions.**
- **Big integers that JSON can't hold safely** (PRNG outputs) are decimal strings.

When a fixture fails, the runner reports the file, the step and the exact field, e.g.
`nim-normal-win.json [Normal game] step 2 (seat 1): state.pile: expected 1, got 2`.

## Ludo (`ludo/`)

Board model (ADR-0008):

| Position | Meaning |
|---|---|
| `-1` | in base |
| `0..50` | the shared track, counted from the token's own start square |
| `51..55` | the token's own home column (it can't be captured there) |
| `56` | home |

- A seat of colour `c` at relative position `p` (0..50) is on absolute square `(13c + p) mod 52`.
- Two players play colours 0 and 2, three play 0, 1 and 2, and four play all four.
- Safe absolute squares are `0, 13, 26, 39` (the starts) and `8, 21, 34, 47` (the stars).

Fields:

| Field | Shape |
|---|---|
| `config` | the house-rule toggles: `sixToLeaveBase`, `threeSixesForfeit`, `captureBonus`, `mustCapture`, `safeSquares`, `quickMode`, `placings`, `teams`. Anything left out takes its Classic Naija default. |
| `config.dice` | **test only**: the die values to roll, in order. Every listed value must be used. Once the list runs out, the dice come from the seeded PRNG again. |
| actions | `{"roll": true}` or `{"move": tokenIndex}` |
| `state` | `tokens` (4 per seat), `currentSeat`, `phase` (`roll`, `move` or `over`), `lastRoll`, `sixesInRow`, `finished` (seats in finishing order), `colors`, `hasDice` (false in a player's view), `moveNumber` |
| `initialState` | the same fields. The dice there start from seed 1. |

`seeded-dice.json` has no scripted dice. It pins the real PRNG rolls for seed 42.
