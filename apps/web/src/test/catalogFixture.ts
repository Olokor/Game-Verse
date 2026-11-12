import type { components } from '@gameverse/api-schema';

type GameInfo = components['schemas']['GameInfo'];

const game = (id: string, title: string, tagline: string, category: string, status = 'coming_soon'): GameInfo => ({
  id,
  title,
  tagline,
  category,
  accentToken: `color.category.${category}`,
  version: '0.1.0',
  status,
  modes: [{ key: 'vs-ai', kind: 'vs_ai', label: 'vs AI', playerCounts: [2] }],
});

/**
 * What GET /catalog/games returns for the launch library (same order and statuses as the backend seed,
 * backend/src/GameVerse.Platform/Catalog/CatalogSeed.cs). Used by Vitest (src/test/setup.ts) and Playwright (e2e).
 */
export const catalogFixture: GameInfo[] = [
  game('ludo', 'Ludo', 'Roll, race, capture. 2 to 4 players.', 'board', 'enabled'),
  game('whot', 'Whot', 'The Naija card classic. Hold on, pick two.', 'cards'),
  game('draughts', 'Draughts', 'Jump, crown, clear the board.', 'board'),
  game('chess', 'Chess', 'Rated games with clocks.', 'brain'),
  game('twin-lanes', 'Twin Lanes', 'Two cars, one brain.', 'arcade'),
  game('wahala', 'Wahala', 'Every level lies to you.', 'adventure'),
  game('pool', 'Pool', 'Sink your set, then the eight.', 'arcade'),
  game('downhill', 'Downhill', 'Race your own ghost to the bottom.', 'racing'),
  game('stickman-combat', 'Stickman Combat', 'Combo through the campaign.', 'fight'),
  game('super-race', 'Fabulous Super Race', 'Race through worlds with personality.', 'racing'),
  game('rival-realms', 'Rival Realms', 'One on one, with an original roster.', 'fight'),
];
