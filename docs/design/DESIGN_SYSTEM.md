# GameVerse Design System

The goal: GameVerse should feel like a console dashboard that happens to live on a phone. Calm, dark, confident shell. Loud, colorful games. The shell is the frame, the games are the art.

All values here live in `packages/design-tokens/tokens.json`. Components read tokens, never raw values.

---

## 1. Principles

1. **The game is the color.** The shell is near-monochrome. Game cards, thumbnails and in-game screens bring the color. This is what stops the UI from looking like a template.
2. **One primary action per screen.** Play is always the most obvious thing.
3. **Fast beats fancy.** Navigation animations under 300ms. Anything that delays play gets cut.
4. **Thumb-first.** Primary actions sit in the bottom 40% of the screen on mobile.
5. **Earned celebration.** Big moments (win, level up, rank up, chest) get full-screen motion. Everyday moments stay quiet.
6. **Low-end Android is the target device.** If it stutters on a 3GB RAM phone, it is not done.

## 2. Color tokens (dark-first)

| Token | Value | Use |
|-------|-------|-----|
| `bg.base` | `#0A0B10` | App background |
| `bg.surface` | `#12141C` | Cards, sheets |
| `bg.elevated` | `#1A1D28` | Modals, popovers, pressed cards |
| `bg.inset` | `#07080C` | Inputs, wells, board backgrounds |
| `border.subtle` | `#232736` | Dividers, card borders |
| `border.strong` | `#343A4F` | Focus-adjacent, active outlines |
| `text.primary` | `#F2F3F7` | Headings, body |
| `text.secondary` | `#9AA0B4` | Supporting text |
| `text.muted` | `#5E6478` | Hints, disabled |
| `brand.volt` | `#C8FF3D` | THE brand color. Primary buttons, active tab, XP. Use sparingly. |
| `brand.voltInk` | `#0A0B10` | Text on volt |
| `accent.flare` | `#FF5A36` | Live, hot, urgency, tournaments |
| `accent.sky` | `#4DA3FF` | Info, friends online, links |
| `state.success` | `#3DDC97` | Wins, confirmations |
| `state.danger` | `#FF4D6A` | Losses, destructive |
| `state.warning` | `#FFC23D` | Coins, warnings |
| `currency.coin` | `#FFC23D` | Coin icon and values |
| `currency.gem` | `#B07CFF` | Premium currency (later) |

**Category colors** (used only on category chips, card glows and game headers):
Board `#3DDC97`, Cards `#FF5A36`, Arcade `#C8FF3D`, Racing `#4DA3FF`, Fight `#FF4D6A`, Adventure `#B07CFF`, Brain `#FFC23D`.

**Rank colors:** Bronze `#C98A5A`, Silver `#B9C2D0`, Gold `#FFC23D`, Platinum `#7FE3E0`, Diamond `#7FA8FF`, Legend `#C8FF3D`.

Gradients: allowed only inside game art and thumbnails, and as a single soft radial glow behind a hero element. Never on buttons, never on surfaces.

Light theme: not in MVP. Tokens are structured so it can be added.

## 3. Typography

| Role | Font | Notes |
|------|------|-------|
| Display | **Clash Display** (Fontshare, free commercial use) | Game titles, big numbers, rank-ups. Semibold/Bold. |
| UI / body | **Satoshi** (Fontshare) | Everything else. |
| Numeric | Satoshi with tabular figures | Scores, timers, currency, so digits don't jitter. |

Confirm both licenses before shipping. Fallback: `Space Grotesk` + `Inter`.

Scale (px, mobile): 12 caption, 14 body-sm, 16 body, 18 title-sm, 22 title, 28 headline, 36 display, 56 hero-number.
Line height 1.4 for body, 1.1 for display. Letter spacing -0.02em on display sizes. Uppercase only for tiny labels (11–12px, +0.08em tracking).

## 4. Space, radius, elevation

- Spacing: 4pt grid. Tokens: 4, 8, 12, 16, 20, 24, 32, 40, 56, 72.
- Screen gutter: 16 mobile, 24 tablet, 32+ desktop.
- Radius: `sm 8`, `md 14`, `lg 20`, `xl 28`, `pill 999`. Game cards use `lg`. Buttons use `md` or `pill`.
- Elevation is expressed with surface color steps and a 1px border, not heavy shadows. Exception: one soft colored glow on the focused or featured game card.

## 5. Motion

| Token | Duration | Easing | Use |
|-------|----------|--------|-----|
| `motion.instant` | 90ms | ease-out | Press feedback |
| `motion.fast` | 160ms | `cubic-bezier(0.2, 0.8, 0.2, 1)` | Toggles, chips, small reveals |
| `motion.base` | 240ms | same | Screen transitions, sheets |
| `motion.slow` | 380ms | spring (stiffness 300, damping 26) | Hero card expansion, results |
| `motion.celebrate` | 900–1600ms | choreographed (Rive) | Win, level up, rank up, chest |

Rules:
- Every pressable scales to 0.96 on press, with a light haptic on mobile.
- Game card to game lobby is a shared-element transition: the card expands into the lobby header.
- Numbers that change (coins, XP, score) count up, never jump.
- Respect reduced-motion settings: replace motion with fades.

## 6. Core components

Build these first, in both web (React) and Unity (UI Toolkit), driven by the same tokens:

- **Button:** primary (volt), secondary (surface + border), ghost, danger. Sizes sm/md/lg. States: default, pressed, hover (web), focus, disabled, loading.
- **GameCard:** thumbnail art, title, category chip, live player count, optional "New" or "Event" badge. Variants: hero (wide), standard (portrait 3:4), compact (row).
- **ModeTile:** big tappable tile for Quick Match / Private Room / vs AI / Local.
- **PlayerChip:** avatar + frame + name + rank badge + online dot.
- **CurrencyPill:** coin/gem icon + tabular amount, animates on change.
- **XPBar:** level number, progress, count-up animation.
- **QuestCard:** title, progress bar, reward preview, claim state.
- **Sheet:** bottom sheet on mobile, centered modal on desktop.
- **Toast** and **InviteBanner** (friend invites appear as a banner with accept/decline).
- **Skeletons** for every list and card.
- **RoomCodeDisplay:** big monospace code, copy, share, QR.
- **ResultsScreen:** platform-owned, shows outcome, XP gained, quest progress, rematch.

## 7. Screen inventory (MVP)

1. Splash and first-run (guest play in one tap, sign up later)
2. Home: hero featured game, continue playing row, daily quests, categories, friends online
3. Browse by category
4. Game lobby: hero art header, mode tiles, house rules, leaderboard peek
5. Matchmaking: animated search, estimated wait, cancel, "play vs AI while you wait"
6. Private room: room code, seats, ready states, rules, invite friends
7. In-game HUD frame (platform overlay: pause, quick chat, connection status)
8. Results
9. Profile: banner, level, ranks per game, stats, showcase cosmetics
10. Leaderboards: global, friends, country; daily/weekly/season
11. Friends and invites
12. Quests and season pass
13. Store (cosmetics for coins only in MVP)
14. Settings

Mobile navigation: bottom tab bar with 5 items: Home, Games, Play (center, volt), Social, Profile.
Web navigation: left rail on desktop, bottom bar on mobile web.

## 8. Game thumbnails and art direction

- Each game has a key art image at 3 sizes (hero 16:9, card 3:4, icon 1:1) plus a dominant accent color stored in the catalog.
- Consistent style across all thumbnails: bold silhouette, single light source, dark backdrop that sits on `bg.base`. Commission or produce as a set so the library looks curated.
- No stock-photo or default-asset look. Free asset packs (e.g. Kenney, CC0) are acceptable for prototyping only, never for shipping key art.

## 9. Sound and haptics

- UI sounds: short, soft, consistent set (tap, open, close, success, error, coin, level up).
- Haptics: light on press, medium on match found, success pattern on win.
- Every sound and haptic can be toggled independently.

## 10. Accessibility

- Text contrast at least 4.5:1 on body text.
- Touch targets at least 44x44.
- Color is never the only signal (Ludo tokens have shapes, Whot suits have clear symbols, rank badges have icons).
- Supports system font scaling up to 130% without layout breaking.

## 11. Performance budgets

- Cold start to interactive home: under 4s on a mid-range Android.
- Home screen at 60fps while scrolling.
- Web: initial shell JS under 200KB gzipped. Unity game builds lazy-loaded per game, compressed with Brotli.
- Thumbnails served as WebP/AVIF with size variants.
