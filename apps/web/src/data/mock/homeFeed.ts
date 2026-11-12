// TODO(gameverse): mock data. Games now come from the catalog API (src/data/catalog.ts); these mocks remain for the
// component gallery (/dev/components) and for the Home sections without an API yet: quests (Prompt 2.1) and friends
// (2.3). They mention only playable games, so the screen never contradicts the catalog.
import type { Friend, GameSummary, HomeFeed, Quest } from '../types';

/** Gallery examples covering every card state (playable, coming soon, badges, live count). */
export const mockGames: Record<string, GameSummary> = {
  ludo: { id: 'ludo', title: 'Ludo', category: 'board', tagline: 'Roll, race, capture. 2 to 4 players.', playable: true, livePlayers: 12840, badge: 'event' },
  whot: { id: 'whot', title: 'Whot', category: 'cards', tagline: 'The Naija card classic. Hold on, pick two.', playable: true, badge: 'new' },
  draughts: { id: 'draughts', title: 'Draughts', category: 'board', tagline: 'Jump, crown, clear the board.', playable: false },
  chess: { id: 'chess', title: 'Chess', category: 'brain', tagline: 'Rated games with clocks.', playable: false },
  'twin-lanes': { id: 'twin-lanes', title: 'Twin Lanes', category: 'arcade', tagline: 'Two cars, one brain.', playable: false },
  wahala: { id: 'wahala', title: 'Wahala', category: 'adventure', tagline: 'Every level lies to you.', playable: false },
  downhill: { id: 'downhill', title: 'Downhill', category: 'racing', tagline: 'Race your own ghost to the bottom.', playable: false },
  'stickman-combat': { id: 'stickman-combat', title: 'Stickman Combat', category: 'fight', tagline: 'Combo through the campaign.', playable: false },
};

export const mockQuests: Quest[] = [
  { id: 'q1', title: 'Win 2 games of Ludo', progress: 1, goal: 2, reward: { kind: 'coin', amount: 150 }, claimed: false },
  { id: 'q2', title: 'Play Ludo against the AI', progress: 1, goal: 1, reward: { kind: 'xp', amount: 200 }, claimed: false },
  { id: 'q3', title: 'Capture 5 tokens', progress: 5, goal: 5, reward: { kind: 'coin', amount: 100 }, claimed: true },
];

export const mockQuestsResetInMinutes = 372;

export const mockFriends: Friend[] = [
  { id: 'f1', displayName: 'Tobi', rank: 'gold', presence: 'in-game', activity: 'Ludo' },
  { id: 'f2', displayName: 'Adaeze', rank: 'diamond', presence: 'online' },
  { id: 'f3', displayName: 'Kunle', rank: 'silver', presence: 'online' },
  { id: 'f4', displayName: 'Halima', rank: 'platinum', presence: 'in-game', activity: 'Ludo' },
  { id: 'f5', displayName: 'Emeka', rank: 'bronze', presence: 'offline' },
];

/** A complete feed for the gallery and tests that need one without the API. */
export const mockHomeFeed: HomeFeed = {
  featured: mockGames.ludo,
  comingSoon: [mockGames.draughts!, mockGames.chess!],
  continuePlaying: [{ game: mockGames.ludo!, status: 'vs AI, level 2', yourTurn: true }],
  quests: mockQuests,
  questsResetInMinutes: mockQuestsResetInMinutes,
  categories: [
    { category: 'board', label: 'Board', gameCount: 2 },
    { category: 'brain', label: 'Brain', gameCount: 1 },
  ],
  friends: mockFriends,
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
