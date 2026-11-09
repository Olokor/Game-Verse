# ADR-0002: Development environment, tooling and open stack choices

- **Status:** Accepted
- **Date:** 2026-11-09
- **Deciders:** Moses (owner), Claude Code (tech lead)
- **Resolves:** the "Sub-choices left open" in ADR-0001, and the setup questions from the Prompt 0.1 repo review

## Context

ADR-0001 locked the core stack but left five choices open. The Prompt 0.1 review found more gaps:

- The plan assumed a Linux workstation, but the owner develops on Windows 11.
- No package managers or CI system had been chosen.
- Large binary assets had no storage strategy.
- Line endings weren't controlled. Git was converting files to CRLF, which breaks shell scripts and Docker entrypoints.
- `.gitignore` assumed Unity sat at the repo root.

## Decision

### Development environment

- **OS:** Windows 11, with **Docker Desktop on the WSL2 backend** running the local stack. Unity runs natively on Windows.
- **Prerequisites:** Docker Desktop, Python 3.12+, Node 22 LTS, pnpm, uv, GNU Make, Unity Hub + Unity 6 LTS (Android Build Support), and Git LFS.
- **Commands:** `make` targets stay as the single entry point (`CLAUDE.md` section 10). On Windows, GNU Make is installed with `winget install ezwinports.make`. Targets must work from Git Bash and from PowerShell. Anything platform-sensitive runs inside a container.
- **Unity tests:** headless test commands are documented for Windows first. Linux commands are added when CI needs them.

### Package managers

- **Python:** `uv`, with `pyproject.toml` and `uv.lock` committed.
- **Node:** `pnpm`, with a pnpm workspace at the repo root covering `apps/web` and `packages/*`. `pnpm-lock.yaml` is committed.

### CI

- **GitHub Actions** (the repo is hosted at `github.com/Olokor/Game-Verse`).
- The first workflows are added with Prompt 0.2 so they test real code: lint, type check, backend tests and a secret scan. Unity CI comes later and needs a Unity license secret.

### Repository hygiene

- **Line endings:** `.gitattributes` forces LF for all text files. CRLF is kept only for `.bat`, `.cmd` and `.ps1`.
- **Unity YAML** (`.unity`, `.prefab`, `.asset`, `.meta` and similar) is stored as text so it can be diffed. The project must use Force Text serialization and visible meta files.
- **Large binaries** (art, audio, video, 3D models, fonts, Rive files, archives) are stored with **Git LFS**. LFS is enabled per repository (`git lfs install --local`) so it doesn't change other repos on the machine.
- **Ignore rules:** the root `.gitignore` covers Python, Node, environment files, editors and OS files. Unity's rules live in `apps/mobile/.gitignore`, where their `/Library/`-style patterns resolve correctly. `.env` and `.env.*` are ignored; only `.env.example` is committed.

### Open stack choices from ADR-0001

| Choice | Decision | Why |
|---|---|---|
| ASGI server | **Uvicorn** | Fast, widely used, works with Channels. It is also what Prompt 0.2 already assumed. |
| Code tweens | **LitMotion** | Zero-allocation tweens built on Unity's job system. Better fit for low-end Android than DOTween, and MIT licensed. |
| Async in Unity | **UniTask** (added to the locked stack) | Allocation-free async/await for Unity. Already used by `IGameModule` in `CLAUDE.md` 4.2. |
| C# client generator | **NSwag** | Mature OpenAPI-to-C# generator. Run from a script, with output committed to the Unity project. Using it avoids writing and maintaining our own generator. |
| Unity 6 LTS version | **Newest Unity 6 LTS at install time** | The exact version is pinned in `apps/mobile/ProjectSettings/ProjectVersion.txt` when Prompt 0.5 creates the project. Upgrades are deliberate and recorded. |

### Product scope

- **No ads of any kind in the MVP,** including the rewarded hint ad mentioned for Wahala. Revisit after launch, with owner approval.

## Consequences

- One `make` entry point on every OS. Windows developers install GNU Make once.
- Lock files make installs reproducible across machines and CI.
- LFS keeps clones small. LFS storage and bandwidth on GitHub have quotas, so monitor them once real art lands.
- LF everywhere avoids "works on my machine" breakage in containers. Existing files have been renormalized.

## Follow-ups

- Prompt 0.6's web performance decision becomes **ADR-0003** (renumbered because this ADR took 0002).
- These owner questions are still open and don't block Phase 0 code:
  - Is the GameVerse name and the `gameverse.app` domain cleared?
  - What is the minimum age, and how is it collected?
  - Which hosting provider, region and budget?
  - Are the Clash Display and Satoshi fonts licensed for embedding in apps?
  - Is the repo private, and is there a license?
