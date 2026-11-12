# GameVerse Game Briefs

Each brief defines the pitch, the core loop, modes, authority model, MVP scope and what makes our version different. Anything marked **Open question** must be confirmed with the owner before implementation.

Shared across every game:
- Earns account XP, game mastery XP, coins and quest progress through `progression` events. Games never grant rewards directly.
- Cosmetics are loaded from the player's inventory via the SessionManifest.
- Every game has: tutorial (skippable, under 60 seconds), pause, settings (sound, music, haptics), rematch where multiplayer applies, and a results screen built by the platform.

---

## 1. Ludo

**Pitch:** The Ludo every Nigerian already knows, played the way their family plays it, with fair dice you can actually verify.

**Core loop:** Roll, choose a token, move, capture, race all four home (all eight in teams).

**Modes:** Local pass-and-play (2 to 4), vs AI (3 levels), online quick match (2 or 4 players), private room, teams 2v2.

**House rules (room-configurable toggles), defaults confirmed by the owner 2026-11-12 ("Classic Naija", ADR-0008):**
- Six required to leave base (default on)
- Rolling three sixes forfeits the turn; the third six isn't moved (default on)
- Capture grants a bonus roll (default on)
- Must capture when possible (default off)
- Safe squares: the 4 start squares and 4 star squares (default on)
- Quick mode: each player starts with one token already out (default off)

**Always on:**
- A six gives another roll.
- An exact roll is needed to reach home.
- Reaching home gives another roll.
- There are no blocks: landing on a square captures every opponent token there.

**Game end (players choose):**
- `placings` is 1, 2 or 3: the game ends when that many players have finished. It's capped at players minus 1.
- The default is 1: the first player home wins.
- The rest rank by tokens home, then progress.

**Teams 2v2:**
- Partners sit opposite and never capture each other.
- A finished player keeps rolling and moves the partner's tokens.
- The team wins when all 8 tokens are home.

**Leaving:**
- A player may resign at any time. The server forfeits a seat after the disconnect grace window.
- Leavers are skipped and rank last.
- In teams, a leaver forfeits for the team.

**Authority:**
- The server rolls dice with commit-reveal. It publishes the hash of a secret at creation; the seed is derived from that secret, which is revealed via `/sessions/{id}/fairness` after the match.
- The server validates every token move.

**AI:**
- Level 1: random legal move.
- Level 2: heuristic (capture > reach home > leave base > enter home column > escape danger > safe square > progress).
- Level 3: expectimax over one roll.

**Feel:** Dice roll is the hero moment. Physical dice tumble with sound, token hops square by square with a small squash, capture sends the victim flying back to base with a comic sound. Board themes as cosmetics (classic wood, Ankara print, neon, marble).

**Accessibility:** Token colors also carry a shape/pattern so colorblind players can play.

**MVP:** 2 and 4 player online, private rooms, vs AI, local (2 to 4, including 3 players). The engine already supports all 6 toggles, placings and teams 2v2; which of them online rooms expose at launch is a UI decision (Prompt 1.7).

---

## 2. Whot

**Pitch:** The card game of Nigerian school breaks and family gatherings, with the trash talk built in.

**Deck:** 54 cards across Circle, Triangle, Cross, Square, Star and 5 Whot (20) cards. Exact card distribution must follow standard Nigerian Whot. Put the deck definition in a data file, not in code.

**Special cards (default ruleset, all configurable as variants):**
- 1 Hold On: play again
- 2 Pick Two: next player picks two unless they defend
- 5 Pick Three (variant toggle)
- 8 Suspension: next player skips
- 14 General Market: everyone else picks one
- 20 Whot: wild, player requests a shape

**Open question:** Which defending/stacking rules are default? (e.g. can a Pick Two be countered with another Two?) Regional rules differ. Ship two presets: "Classic" and "Lagos Rules", owner defines both.

**Authority:** Server shuffles and deals. Each client only ever receives its own hand plus card counts for others. Must announce "Last Card" and "Check Up" with penalty if forgotten, enforced server-side.

**Feel:** Card fan in hand with drag-to-play and tap-to-play. Quick-chat presets in local flavor ("Pick two, abeg!", "Check up!", "You don chop!"). Voice SFX for General Market and Check Up.

**MVP:** 2 to 4 players online, private rooms, vs AI, Classic ruleset.

---

## 3. Draughts (Checkers)

**Pitch:** Both the 8x8 game the world knows and the 10x10 international draughts widely played in Nigeria.

**Variants:** English 8x8 and International 10x10 (flying kings, backward captures, majority capture rule). Variant is a config field, one engine with rule flags.

**Authority:** Server engine validates, enforces mandatory and maximum captures.

**AI:** Alpha-beta search with depth by difficulty. Runs client-side for vs AI and offline.

**MVP:** Both variants, online + private + vs AI, rated ranked queue.

---

## 4. Chess

**Pitch:** Clean, fast, beautiful chess with real ratings.

**Modes:** Bullet 1+0, Blitz 3+2, Rapid 10+0, vs AI, private room, daily puzzle.

**Authority:** Server validates with our own move generator in `GameVerse.Rules/Chess` (bitboards, verified against standard perft counts). Server owns the clocks. Draw by repetition, 50-move rule, insufficient material, stalemate, resign, draw offer.

**AI:** Stockfish for higher levels, run server-side as a separate process or container (Stockfish is GPL; do not bundle it into the client app without owner sign-off on licensing). Lower levels use a small client-side engine.

**Extras:** Premoves, move list with PGN export, post-game "key moments" review using engine eval.

**MVP:** Blitz and Rapid online, vs AI, private rooms, Glicko-2 rating.

---

## 5. Twin Lanes (inspired by 2 Cars)

**Reference:** In the original, the player steers two cars at once, each in its own two-lane half of the road, tapping the left or right side of the screen to switch that car's lane while collecting circles and dodging squares.

**Our version, what makes it ours:**
- **Modes:** Classic endless; Rush (90-second score attack); **Duo** where two people on one phone each control one car; **Trio Hell** unlock with three cars and three tap zones.
- **Obstacle language beyond circles and squares:** shifting gates, lane-swap zones that invert controls for 3 seconds, magnet pickups.
- **Daily Run:** same seed for everyone that day, one leaderboard, resets at midnight WAT.
- **Visual identity:** each season reskins the road (Lagos night, Harmattan haze, rainy season with puddle reflections).

**Authority:** Seeded generation. Client submits seed + tap timestamps. Server re-simulates (deterministic, simple) to verify the score.

**Tech:** 2D, tiny build, perfect first web game and first proof of score verification.

**MVP:** Classic, Rush, Daily Run, leaderboards.

---

## 6. Wahala (our take on Die Again / troll platformers)

**Reference:** Troll platformers like Die Again: Troll Game Ever are built on deception: floors that vanish, spikes that appear after you pass, doors that run away. Players die constantly, laugh, and retry. Player reviews of existing mobile titles complain loudly about ad overload. That is our opening.

**Working name:** Wahala. **Open question:** final name. "Die Again" is used by several existing games, so we will not use it.

**Our pillars:**
1. **Comedy over cruelty.** Every trap is a joke with a punchline. Death animation is funny, restart is instant (under 300ms, no screen, no ad).
2. **Ghost deaths.** You see faint markers where other players died on this level. A cluster of 4,000 skulls on one innocent-looking tile is its own joke and a warning.
3. **Death counter as a badge.** Per-level and lifetime deaths are shown proudly, shareable as an image card.
4. **Level Maker (phase 2, the retention engine).** Players build troll levels from the same trap kit, must beat their own level before publishing, others play and rate. Daily featured community level.
5. **World themes:** Office (the printer is a trap), Danfo Park, Haunted Palace, Space Station, Wahala Market.
6. **No ad after death. Ever.** Optional rewarded ad for a hint only.

**Trap kit (data-driven, each trap is a prefab with parameters):** fake floor, delayed spike, homing ceiling, runaway door, reverse-gravity zone, fake checkpoint, invisible block, troll coin, chasing wall, physics-swapped controls.

**Authority:** Single player. Level completion submits input log for verification on featured/ranked levels.

**MVP:** 30 handcrafted levels across 3 worlds, ghost deaths, death counter, share card.

---

## 7. 8-Ball Pool

**Pitch:** Smooth, satisfying pool with real spin and honest physics.

**Modes:** vs AI (practice), 1v1 online, private room, trick shot challenges (daily).

**Physics:** Custom 2D deterministic physics, not Unity's PhysX (not deterministic across devices). Fixed-point math, fixed timestep. Handles ball-ball collision, cushions, pockets, rolling/sliding friction, top/back/side spin approximation. The simulation lives once in `GameVerse.Rules/Pool` and runs identically on server and client. Fixtures in `shared/rules-fixtures/pool/` lock expected final positions for known shots so any change that alters results is caught.

**Authority:** Shooter submits angle, power, spin offset. Server simulates the official result and broadcasts the outcome. Clients animate from the same parameters and reconcile to server final positions.

**Rules:** Standard 8-ball: break, open table, solids/stripes assignment, fouls (scratch, wrong ball first, no rail after contact), ball in hand, calling the 8.

**Feel:** Aim guide with first-collision ghost ball (length reduced in ranked), power by pull-back drag, spin selector on cue ball widget, satisfying pocket sounds, slow-motion on the 8.

**MVP:** vs AI and 1v1 online with standard rules.

---

## 8. Downhill (bicycle)

**Pitch:** 2D side-view physics downhill. Lean, pump, flip, land clean, beat your ghost.

**Mechanics:** Rider and bike as a physics rig (wheel joints). Controls: brake/accelerate, lean back/forward. Tricks: flips and wheelies grant boost. Crash on head impact.

**Modes:** Career tracks (stars for time), Ghost race (vs your best, friend's best, or world record ghost), Daily Track.

**Worlds:** Forest Trail, Quarry, Rooftops, Mountain Pass, Night Neon.

**Authority:** Input-log replay for ranked times. Server rejects impossible times outright.

**MVP:** 20 tracks across 2 worlds, ghosts, stars.

---

## 9. Stickman Combat

**Pitch:** A stylish level-based stickman beat 'em up with escalating enemy waves, weapons and bosses.

**Structure:** Chapters of 10 levels. Each level has waves, the 10th is a boss. Difficulty climbs through enemy types, not just HP inflation.

**Combat:** Tap/swipe combos (light, heavy, dash, block/parry, special meter), juggles, environmental kills, weapon pickups (staff, sword, bat).

**Enemy roster grows:** grunt, shield, ranged, ninja (dodger), brute, summoner, mini-bosses with readable telegraphs.

**Progression:** Skill tree (new moves), gear with stat modifiers, outfit cosmetics. Endless Arena mode unlocks after chapter 1.

**Feel:** Hit-stop on impact, screen shake within budget, slow-mo on finishing blow, ragdoll knockbacks.

**MVP:** Chapter 1 (10 levels + boss), Endless Arena.

---

## 10. Fabulous Super Race

**Pitch:** A vibrant 3D arcade racer that travels through worlds with personality, each world with a signature hazard and a signature shortcut.

**Open questions for the owner (we will workshop these):**
1. Kart-style with items and weapons, or pure racing with drift and boost?
2. Vehicle variety: cars only, or cars plus bikes, boats, hover vehicles in some worlds?
3. Camera: chase cam 3D, or isometric 2.5D (lighter, better for web and low-end Android)?
4. Online: ghosts first and real-time later (recommended), or real-time at launch?
5. Story layer: a tour/championship narrative with rivals, or pure event list?

**Proposed worlds (draft for discussion):**

| World | Look | Signature hazard | Signature shortcut |
|-------|------|------------------|--------------------|
| Lagoon City Nights | Neon coastal megacity, long bridges over water, bus traffic | Traffic lanes that merge without warning | Jump ramp off a half-built flyover |
| Harmattan Dunes | Sahel desert, gold light, ruined trading towns | Sandstorm cuts visibility, dunes shift between laps | Ride a dry riverbed through a canyon |
| Cloud Plateau | Mountain plateau above the clouds, cable cars, switchbacks | Fog banks and sheer drops with no barriers | Cable-car rail grind between peaks |
| Seven Falls Rainforest | Waterfall jungle, rope bridges, ancient stone | Slippery mud zones and falling logs | Drive behind the waterfall |
| Wahala Market | Packed open market, stalls, rooftops | Stalls and carts that move into the track | Rooftop route over the market |
| Volcano Forge | Industrial forge inside a volcano | Lava surges on a timer | Magma tube boost tunnel |
| Frostline Glacier | Ice canyons, aurora sky | Ice physics, cracking ice that drops a lane | Slide under an ice arch |
| Sky Archipelago | Floating islands linked by light bridges | Bridges flicker in and out | Glide between islands |
| Zero-G Moonbase | Low gravity, craters, domes | Low gravity makes every jump long and risky | Loop around the outside of a dome |

**Progression:** World tour of championships, garage with upgrades (speed, accel, handling, boost) plus cosmetic paints, decals and trails.

**Tech:** URP Forward+, aggressive LOD and baked lighting, target 60fps on mid-range Android. Mobile first. Web may ship a reduced version per world; decide after the Phase 0 web performance spike.

**MVP:** 2 worlds x 3 tracks, 6 vehicles, ghost racing, championship mode.

---

## 11. Rival Realms (original fighting game)

**Pitch:** A 2.5D one-on-one fighter with an original roster and our own lore, not a reskin of any existing franchise.

**Original concept proposal:** Fighters are champions of fictional realms, and their fighting styles draw on real-world traditional martial arts that rarely get screen time, alongside the usual staples. For example: Dambe-style boxing, Laamb-style wrestling, Engolo-inspired kicks, stick fighting, alongside capoeira, muay thai, kung fu and street brawling. Characters are fictional; styles inform the moveset and animation. **Open question:** confirm the concept and tone (grounded vs fantasy powers).

**Mechanics:** Light/medium/heavy, special moves with simplified inputs on touch (swipe + button), super meter, block/parry, throws, a stylized finisher per character (dramatic, not gory, keeps a broad age rating).

**Modes:** Story ladder, vs AI, local versus (two controllers or split touch on tablet), training. Online PvP is a later phase needing rollback netcode on a dedicated service.

**MVP:** 6 fighters, 3 stages, story ladder, vs AI, training.

---

## 12. Retention games to add later (ranked by value)

These are chosen because they create daily habits, social pull or appointment play, not because they are trendy.

| Game | Why it keeps people coming back | Effort |
|------|----------------------------------|--------|
| **Ayo (Ayo Olopon)** | Yoruba mancala, deeply familiar, fast turn-based, nobody does it beautifully. Cultural ownership. | Low |
| **Penalty Kings** | 30-second football penalty duels, PvP, huge appeal in a football-mad market. | Medium |
| **Daily Word** | One puzzle a day, shareable result grid, word packs in English and Pidgin, later Yoruba, Igbo, Hausa. Wordle-style daily habit. | Low |
| **Live Trivia Night** | Scheduled live quiz at a fixed time (e.g. 8pm WAT) with everyone playing at once. Appointment gaming drives returns. Prizes are cosmetics and coins in MVP. | Medium |
| **Snake Arena (.io style)** | Drop-in real-time arena, sessions of 2 to 5 minutes, endlessly replayable. | Medium |
| **Danfo Empire (idle tycoon)** | Build a transport business that earns while you are away, giving a reason to open the app several times a day. Feeds coins into the platform. | Medium |
| **Sudoku / Brain Gym** | Daily brain workout streak, appeals to older players and parents. | Low |
| **Tower Stack / One-tap arcade** | 30-second sessions for queue waits and ad breaks. | Low |

## 13. Platform reward system

The reward system is platform-wide so playing any game advances everything.

- **Account level:** XP from every game. Levels unlock avatar frames, titles, profile banners.
- **Game mastery:** per-game levels unlock cosmetics specific to that game (dice skins, card backs, cue skins, car paints).
- **Ranked tiers:** per competitive game: Bronze, Silver, Gold, Platinum, Diamond, Legend. Season resets with soft decay.
- **Daily quests (3):** deliberately cross-game ("Win a Whot match", "Score 50 in Twin Lanes", "Capture 5 tokens in Ludo"). This is how players discover new games.
- **Weekly quests:** bigger goals, bigger chests.
- **Login streak:** escalating daily reward, with one free streak freeze per week so a missed day does not punish loyal players.
- **Season pass:** free track for everyone, premium track later (cosmetics only, no gameplay advantage).
- **Clubs:** groups of up to 30 contribute to a weekly club goal, everyone gets the club chest.
- **Tournaments:** free entry, bracket or leaderboard style, rewards are coins, exclusive cosmetics and titles.
- **Collections:** sets of cosmetics across games; complete a set for a bonus.
- **Referral:** invite a friend who reaches level 5, both get a cosmetic.

**Rules we hold ourselves to:**
- Soft currency is earned, never cashed out.
- No randomized rewards purchasable with real money.
- No fake scarcity timers, no punishing streak loss, no ads after death or between turns in multiplayer.
- Players can see exactly what every reward contains before claiming.
