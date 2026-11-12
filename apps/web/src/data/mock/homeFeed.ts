// TODO(gameverse): static mock data until the API exists. Catalog arrives in Prompt 1.2, quests and
// progression in 2.1, friends in 2.3. Replace getHomeFeed() in src/data/api.ts; screens need no change.
import type { CategorySummary, GameSummary, HomeFeed } from '../types';

export const mockGames: Record<string, GameSummary> = {
  ludo: { id: 'ludo', title: 'Ludo', category: 'board', tagline: 'Roll, race, capture. 2 to 4 players.', livePlayers: 12840, badge: 'event' },
  whot: { id: 'whot', title: 'Whot', category: 'cards', tagline: 'The Naija card classic. Hold on, pick two.', livePlayers: 8420, badge: 'new' },
  draughts: { id: 'draughts', title: 'Draughts', category: 'board', tagline: 'Jump, crown, clear the board.', livePlayers: 3150 },
  chess: { id: 'chess', title: 'Chess', category: 'brain', tagline: 'Rated games with clocks.', livePlayers: 5210 },
  'twin-lanes': { id: 'twin-lanes', title: 'Twin Lanes', category: 'arcade', tagline: 'Two cars, one brain.', livePlayers: 2290 },
  wahala: { id: 'wahala', title: 'Wahala', category: 'adventure', tagline: 'Every level lies to you.', livePlayers: 940 },
  downhill: { id: 'downhill', title: 'Downhill', category: 'racing', tagline: 'Race your own ghost to the bottom.', livePlayers: 610 },
  'stickman-combat': { id: 'stickman-combat', title: 'Stickman Combat', category: 'fight', tagline: 'Combo through the campaign.', livePlayers: 1330 },
};

const g = (id: string): GameSummary => {
  const game = mockGames[id];
  if (!game) throw new Error(`mock game ${id} missing`);
  return game;
};

export const mockCategories: CategorySummary[] = [
  { category: 'board', label: 'Board', gameCount: 2 },
  { category: 'cards', label: 'Cards', gameCount: 1 },
  { category: 'brain', label: 'Brain', gameCount: 1 },
  { category: 'arcade', label: 'Arcade', gameCount: 1 },
  { category: 'racing', label: 'Racing', gameCount: 1 },
  { category: 'fight', label: 'Fight', gameCount: 1 },
  { category: 'adventure', label: 'Adventure', gameCount: 1 },
];

export const mockHomeFeed: HomeFeed = {
  featured: g('ludo'),
  continuePlaying: [
    { game: g('whot'), status: 'vs Tobi', yourTurn: true },
    { game: g('chess'), status: 'Rated, 5+0, move 23' },
    { game: g('twin-lanes'), status: 'Best 148, beat it' },
    { game: g('draughts'), status: 'vs AI, level 3' },
    { game: g('wahala'), status: 'Level 7 of 30' },
  ],
  quests: [
    { id: 'q1', title: 'Win 2 games of Ludo', progress: 1, goal: 2, reward: { kind: 'coin', amount: 150 }, claimed: false },
    { id: 'q2', title: 'Play a Whot match with a friend', progress: 1, goal: 1, reward: { kind: 'xp', amount: 200 }, claimed: false },
    { id: 'q3', title: 'Score 100 in Twin Lanes', progress: 100, goal: 100, reward: { kind: 'coin', amount: 100 }, claimed: true },
  ],
  questsResetInMinutes: 372,
  categories: mockCategories,
  friends: [
    { id: 'f1', displayName: 'Tobi', rank: 'gold', presence: 'in-game', activity: 'Whot' },
    { id: 'f2', displayName: 'Adaeze', rank: 'diamond', presence: 'online' },
    { id: 'f3', displayName: 'Kunle', rank: 'silver', presence: 'online' },
    { id: 'f4', displayName: 'Halima', rank: 'platinum', presence: 'in-game', activity: 'Chess' },
    { id: 'f5', displayName: 'Emeka', rank: 'bronze', presence: 'offline' },
  ],
};

/** Mock local player for the shell header. TODO(gameverse): replace with the profile endpoint (Prompt 1.1/2.1). */
export const mockPlayer = {
  displayName: 'Wisdom',
  rank: 'gold' as const,
  level: 12,
  xp: 1240,
  xpRequired: 2000,
  coins: 12500,
  gems: 40,
};
