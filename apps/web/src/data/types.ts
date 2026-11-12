// Client-side view models for the shell. Games come from the catalog API (Prompt 1.2, src/data/catalog.ts); quests,
// friends and progression still come from mocks until their prompts (2.x).

export type Category = 'board' | 'cards' | 'arcade' | 'racing' | 'fight' | 'adventure' | 'brain';

export type RankTier = 'bronze' | 'silver' | 'gold' | 'platinum' | 'diamond' | 'legend';

export interface GameSummary {
  id: string;
  title: string;
  category: Category;
  tagline: string;
  /** Enabled in the catalog. Coming-soon games are listed but cannot be opened (owner decision 2026-11-12). */
  playable: boolean;
  /** Players in game right now. Absent until presence data exists (Realtime): never invented. */
  livePlayers?: number;
  badge?: 'new' | 'event';
}

export interface ContinueEntry {
  game: GameSummary;
  /** Short state line, e.g. "vs Tobi" or "Rated, 5+0, move 23". */
  status: string;
  /** The player is to move in a turn-based match; shown as a chip, not only text. */
  yourTurn?: boolean;
}

export interface Quest {
  id: string;
  title: string;
  progress: number;
  goal: number;
  reward: { kind: 'coin' | 'xp'; amount: number };
  claimed: boolean;
}

export type Presence = 'online' | 'in-game' | 'offline';

export interface Friend {
  id: string;
  displayName: string;
  rank: RankTier;
  presence: Presence;
  /** Game the friend is in, when presence is 'in-game'. */
  activity?: string;
}

export interface CategorySummary {
  category: Category;
  label: string;
  gameCount: number;
}

export interface HomeFeed {
  /** Absent when nothing is featured; Home then starts with Continue playing. */
  featured?: GameSummary;
  /** Listed games that are not playable yet. */
  comingSoon: GameSummary[];
  continuePlaying: ContinueEntry[];
  quests: Quest[];
  questsResetInMinutes: number;
  categories: CategorySummary[];
  friends: Friend[];
}
