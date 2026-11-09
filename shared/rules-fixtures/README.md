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
  "outcome": { "kind": "win", "winningSeats": [0], "reason": "all_tokens_home" }
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
