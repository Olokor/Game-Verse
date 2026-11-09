# @gameverse/design-tokens

`tokens.json` is the single source for every color, font, size, spacing, radius and motion value in GameVerse (`docs/design/DESIGN_SYSTEM.md` sections 2 to 5). Components never hard-code these values. If a value is missing, add it here.

## Commands (from the repo root)

```
make tokens         # regenerate all outputs
make tokens-check   # fail if an output is missing a token or is out of date (also part of `make test`)
node --test packages/design-tokens   # generator tests
```

Node 22+ only, no `npm install` needed: `build.mjs` uses Node built-ins.

## Outputs

All outputs are generated and committed. Never edit them by hand.

| File | Format | Contains |
|---|---|---|
| `apps/web/src/styles/tokens.css` | CSS custom properties on `:root` | every token |
| `apps/admin/src/styles/tokens.css` | same | every token |
| `apps/mobile/Assets/GameVerse/UI/Tokens.uss` | USS variables on `:root` | every token USS can express; the rest are listed in a comment at the end |
| `apps/mobile/Assets/GameVerse/UI/Tokens.g.cs` | C# 9, `GameVerse.UI.GameVerseTokens` | every token, for code tweens, fonts and runtime styling |
| `apps/web/src/styles/theme.css`, `apps/admin/src/styles/theme.css` | Tailwind v4 `@theme inline` | every token with a Tailwind namespace (colors, fonts, sizes, leading, tracking, spacing, radius, easing); the rest are listed in a comment |

**Tailwind mapping.** Utilities read the token variables: `color.bg.surface` becomes `bg-bg-surface`, `space.16` becomes `p-16` (16px, not 4 × 16), `layout.touch-target` becomes `h-touch-target`, `radius.md` becomes `rounded-md`, and `font.size.title` becomes `text-title`. Tailwind's default palette, spacing scale, radii, shadows and easings are reset, so `bg-red-500`, `p-5` or `rounded-2xl` produce nothing. Durations have no Tailwind namespace; use `duration-(--gv-motion-fast-duration)`.

USS has no `line-height`, no numeric font weight and no `cubic-bezier()`, takes only px lengths, and loads fonts as assets rather than family names. Those tokens reach Unity through `Tokens.g.cs` instead. The rule lives in `ussSupports()` in `build.mjs`.

## Naming

Token path `color.brand.volt-ink` becomes:
- CSS/USS: `--gv-color-brand-volt-ink`
- C#: `GameVerseTokens.Color.Brand.VoltInk` (a `Color32`). A segment that starts with a digit gets a leading underscore: `space.16` becomes `GameVerseTokens.Space._16`.

C# units: px and em values are plain floats, durations are **seconds** (what DOTween and LitMotion take), and easings are `CubicBezier` control points (CSS keywords are converted, e.g. `ease-out` to `(0, 0, 0.58, 1)`).

## Token format

Each leaf token has a `$type`, a `$value` and an optional `$description`. Keys are lowercase kebab-case.

| `$type` | `$value` example |
|---|---|
| `color` | `"#C8FF3D"` or `"#C8FF3D80"` |
| `dimension` | `"16px"`, `"-0.02em"` |
| `number` | `1.4` |
| `duration` | `"160ms"` |
| `cubicBezier` | `[0.2, 0.8, 0.2, 1]` or `"ease-out"` |
| `fontFamily` | `["Manrope"]` (primary first, then any fallbacks) |
| `fontWeight` | `600` |
| `string` | `"tabular-nums"` (a CSS keyword) |

The generator rejects unknown types, malformed values, and names that would collide in CSS or C#.
