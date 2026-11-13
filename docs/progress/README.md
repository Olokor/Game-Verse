# Progress log

The running record of what has been built, step by step, so anyone (or the next working session) can see where things stand and retrace a step. `docs/PROMPTS.md` is the plan; this folder records what actually happened against it.

## How this folder works

- **One file per phase** (`phase-N/README.md`): the prompts in it, their status, and the phase's acceptance check.
- **One file per prompt** (`phase-N/prompt-N.M.md`), with:
  - the goal;
  - what was built, and the commits;
  - the decisions taken (and who took them);
  - how it was verified;
  - open items;
  - the next step.
- **Kept up to date as work happens:** the prompt file is updated in the same commit as each part of the work, not afterwards.
- **Big decisions** still go into ADRs (`docs/architecture/`). These files link to them rather than repeat them.

## Status at a glance

| Phase | Branch | Status |
|---|---|---|
| [Phase 0: foundation](phase-0/README.md) | `phase-0-foundation` | Done, merged into `main` (2026-11-12) |
| [Phase 1: Ludo vertical slice](phase-1/README.md) | `phase-1-ludo-slice` | In progress: Prompts 1.1 to 1.5 done, 1.6 in progress (parts A, B and C committed; D next) |
| Phase 2: progression, social, admin | `phase-2-progression-social` | Not started |
| Phases 3 to 7 | | Not started |

## Where to pick up

**Prompt 1.6, part D (Android build and device acceptance; needs a phone on USB).** See [phase-1/prompt-1.6.md](phase-1/prompt-1.6.md) for the exact state and the next steps.
