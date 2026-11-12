# ADR-0003: Open-source fonts (Bricolage Grotesque + Manrope)

- **Status:** Accepted
- **Date:** 2026-11-09
- **Deciders:** Wisdom (owner), Claude Code (tech lead)
- **Supersedes:** the Clash Display + Satoshi choice in `docs/design/DESIGN_SYSTEM.md` section 3
- **Note:** ADR-0002 stays reserved for the web-delivery decision (Prompt 0.7).

## Context

DESIGN_SYSTEM.md section 3 specified Clash Display (display) and Satoshi (UI/body), both from Fontshare, with "confirm both licenses before shipping" as an open item. Fontshare fonts are free for commercial use under the Fontshare EULA, but they are not open source. The terms belong to the foundry and can change, and redistribution and embedding rules have to be checked per platform. That covers the web bundle, the Unity Android APK and any future native build.

While building the web shell (Prompt 0.5), the owner asked for fonts that are open source and still fit the design fully, or for our own typeface. A custom typeface is months of specialist type design work and is out of scope for Phase 0. It can come back later as a brand project.

## Decision

| Role | Font | License | Why |
|---|---|---|---|
| Display | **Bricolage Grotesque** | SIL Open Font License 1.1 | Characterful grotesque with optical sizes. Punchy at hero and headline sizes, which is the role Clash Display played. Weight axis 200 to 800. |
| UI / body / numeric | **Manrope** | SIL Open Font License 1.1 | Clean geometric sans close to Satoshi in tone, very legible at 12 to 16px on low-DPI Android screens. Weight axis 200 to 800. |

Both have true tabular figures (`font-variant-numeric: tabular-nums`). This was verified in Chromium: "1111" and "0000" render at the same width with `tabular-nums` and at different widths without it, for both fonts. Scores, timers and currency therefore don't jitter, as section 3 requires.

Delivery:
- **Web:** self-hosted from the `@fontsource-variable/*` npm packages, bundled by Vite, with no third-party font CDN. Only the weight-axis files are used (latin is 41 KB for Bricolage and 25 KB for Manrope). The latin-ext and vietnamese subsets load only when a page uses those characters. They cover Yoruba, Igbo and Hausa diacritics (ẹ, ọ, ṣ, ŋ, ɗ). See `apps/web/src/styles/fonts.css`.
- **Unity:** the same OFL font files are imported as TextMesh Pro / UI Toolkit font assets in Prompt 0.6.
- **Tokens:** `font.family.display` and `font.family.body` in `packages/design-tokens/tokens.json` hold the family names.

## Alternatives considered

- **Space Grotesk + Inter** (the old fallbacks). Safe and readable, but Inter is everywhere. That works against the "no generic template look" rule in CLAUDE.md section 6.
- **Unbounded + Plus Jakarta Sans.** Strong "gaming" personality, but Unbounded is very wide and loud. That suits game titles but not a calm shell.
- **Keep Fontshare.** Not open source. Licence review is needed per distribution channel.

## Consequences

- No licence review is needed per platform: OFL allows bundling, embedding and modification. The only obligation is to keep the licence with the font files and not to sell the fonts by themselves.
- If the brand later commissions a custom typeface, only the two `font.family.*` tokens and the font files change.
