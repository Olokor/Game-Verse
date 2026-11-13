# Phase 0: foundation

- **Branch:** `phase-0-foundation`
- **Status:** done. Merged into `main` as `e9f28be` (2026-11-12) and pushed.

## Prompts

| Prompt | What | Main commits | Status |
|---|---|---|---|
| 0.1 to 0.3 | Project context, ADR-0001 (C# end to end), repository tooling (LF, Git LFS, gitignore) | `60897a5`, `6bb4bc2`, `6f93945`, `595a376`, `f33343d` | Done |
| 0.4 | Shared libraries `GameVerse.Rules` and `GameVerse.Contracts` (netstandard2.1, C# 9, fixed-point, seeded PRNG, golden fixtures) | `b0919a5` | Done |
| 0.5 | .NET backend skeleton, docker compose stack (Postgres, Redis, API, worker), `/ws/echo` | `ae672a0` | Done |
| 0.6 | Design tokens (`tokens.json` to CSS, USS and C#) and the web shell | `c901e1e`, `bd3929d` | Done |
| 0.6 (mobile) | Unity 6 project skeleton, wire-format parity, IL2CPP Android build, on-device self-check | `38533a0` | Done |
| 0.7 | Unity Web performance spike; result: per-game Web builds (ADR-0002) | `3f7d8ed` to `a9d7273` | Done (spike removed after ADR-0002) |
| 0.8 | Error reporting to self-hosted GlitchTip for API, web and Unity (ADR-0005) | `3a92206`, `f37551f`, `d625e98` | Done |

## Decisions recorded
- ADR-0001: the core stack.
- ADR-0002: per-game Web builds.
- ADR-0003: open-source fonts.
- ADR-0004: the Unity editor version.
- ADR-0005: GlitchTip for error reporting.

## Acceptance
- `make up` is healthy.
- The `/ws/echo` integration test passes.
- `make test` is green.
- The device check logs `GV_SELFCHECK prng ok`.
