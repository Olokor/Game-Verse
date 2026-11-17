# Prompt 1.3: Ludo rules engine

- **Status:** done.
- **Commits:** `987bae2`, `86347e1`, `3380d43`, `7749d32`, `8bbaebc`.
- **ADR:** [ADR-0008](../../architecture/adr-0008-ludo-rules-and-fairness.md)

## What was built
- **`shared/GameVerse.Rules/Ludo`:** every house-rule toggle, placings 1 to 3, teams 2v2, forfeit.
- **43 golden fixtures,** which are the rulebook.
- **AI levels 1 to 3.** L3 beats L1 95/100 times, L2 beats L1 89/100, L3 beats L2 59/100.
- **Dice commit-reveal:** a server secret, a published SHA-256, and `/sessions/{id}/fairness` after the end.
- **The rules-auditor agent** ran, and its gaps were closed.

## Owner decisions
- **Classic Naija defaults:**
  - on: six to leave base, three sixes forfeit, capture bonus, safe squares;
  - off: must capture, quick mode;
  - no blocks.
- **Game end:** the players choose placings 1 to 3 (default 1).
- **Teams:** a finished player moves the partner's tokens.
- **Modes:** vs AI is hosted on the server. Pass-and-play rolls its own dice and is unverified.

## Open items
- A team leaver forfeits for the whole team (tech-lead call; the owner may revisit).
- The AI win-rate tests take 2 to 4 minutes of `make test`.
