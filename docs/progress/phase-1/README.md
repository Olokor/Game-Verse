# Phase 1: Ludo vertical slice

- **Branch:** `phase-1-ludo-slice`
- **Status:** in progress.
- **Partial merge:** Prompts 1.1 to 1.3 were merged into `main` on 2026-11-12 as `5e24c33`, with owner approval, before the phase's acceptance checks. Both branches are pushed.

## Prompts

| Prompt | What | Status | Log |
|---|---|---|---|
| 1.1 | Accounts: guests, email upgrade, tokens, profiles, account deletion | Done | [prompt-1.1.md](prompt-1.1.md) |
| 1.2 | Game catalog and sessions, the SessionManifest, web Home on the real catalog | Done | [prompt-1.2.md](prompt-1.2.md) |
| 1.3 | Ludo rules engine, AI levels 1 to 3, dice commit-reveal | Done | [prompt-1.3.md](prompt-1.3.md) |
| 1.4 | Realtime play (`/ws/play`, match host) and private rooms | Done | [prompt-1.4.md](prompt-1.4.md) |
| 1.5 | Matchmaking queues (casual and ranked) and Glicko-2 ratings | Done | [prompt-1.5.md](prompt-1.5.md) |
| 1.6 | Ludo in Unity: module, platform screens, device acceptance | **In progress** (A and B committed, C being built) | [prompt-1.6.md](prompt-1.6.md) |
| 1.7 | Ludo on web (Unity Web build in `apps/web`), Playwright test | Not started | |

## Phase acceptance (milestone 1 demo)
1. Player A and player B register.
2. A creates a room, and B joins with the code.
3. They play Ludo across a phone and a browser, with the server validating every move.
4. The winner is shown, and a rematch works.

This needs Prompts 1.6 and 1.7. Merge into `main` only after this passes and the owner approves.
