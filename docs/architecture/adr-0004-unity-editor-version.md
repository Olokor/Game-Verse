# ADR-0004: Unity editor version (6.6 now, 6.7 LTS when released)

- **Status:** Accepted
- **Date:** 2026-11-10
- **Deciders:** Moses (owner), Claude Code (tech lead)
- **Amends:** CLAUDE.md section 3 ("Unity 6 LTS")

## Context

CLAUDE.md specifies Unity 6 LTS. When the Unity project was created (Prompt 0.6), the editor on the dev machine was **Unity 6000.6.8f1 (Unity 6.6)**. Unity describes 6.6 as a *Supported* release, not an LTS: it gets LTS-level stability and critical fixes until the next release ships. Unity 6.6 is the last non-LTS release before **Unity 6.7 LTS**, which Unity schedules for Q4 2026. The current LTS is Unity 6.3.

## Decision

Build on **Unity 6000.6.8f1** now, pinned in `apps/mobile/ProjectSettings/ProjectVersion.txt`. Upgrade to **Unity 6.7 LTS** when it is released, as its own small change with a full test run (EditMode, IL2CPP Android build, on-device self-check).

## Why not 6.3 LTS now

- 6.6 to 6.7 is a small, same-generation hop. 6.3 to 6.7 later would be a larger one.
- Nothing in the skeleton depends on 6.6-only features, so the risk of being on a Supported release is low and short-lived.
- It avoids a second ~5 GB editor install now.

## Consequences

- CI images and teammates must use exactly 6000.6.8f1 until the upgrade (GameCI image `unityci/editor:ubuntu-6000.6.8f1-android-3`).
- Package versions are the ones this editor recommends (URP 17.6.0, Input System 1.20.1, Addressables 2.11.2, Localization 1.5.13, Test Framework 1.8.0, Newtonsoft 3.2.2). They move together at the 6.7 upgrade.
- When 6.7 LTS ships: update `ProjectVersion.txt`, package versions, this ADR's status and CLAUDE.md.

## Sources

- Unity 6.6 release announcement: "Unity 6.6 is a Supported release ... until the next release, in this case 6.7, arrives"; 6.7 LTS planned for Q4 (discussions.unity.com, "Unity 6.6 is now available").
