import type { components } from '@gameverse/api-schema';
import { api } from '../lib/api';
import type { Category, CategorySummary, GameSummary, HomeFeed } from './types';
import { mockFriends, mockQuests, mockQuestsResetInMinutes } from './mock/homeFeed';

type GameInfo = components['schemas']['GameInfo'];

const categoryLabels: Record<Category, string> = {
  board: 'Board',
  cards: 'Cards',
  brain: 'Brain',
  arcade: 'Arcade',
  racing: 'Racing',
  fight: 'Fight',
  adventure: 'Adventure',
};

const isCategory = (value: string): value is Category => value in categoryLabels;

/** Catalog game (API) to the card view model. No live player counts: there is no presence data yet (Realtime). */
export function toGameSummary(game: GameInfo): GameSummary | undefined {
  if (!isCategory(game.category)) return undefined; // a category this build does not know yet: skip, don't crash
  return { id: game.id, title: game.title, category: game.category, tagline: game.tagline, playable: game.status === 'enabled' };
}

/** Data access for the catalog. Calls go through this object so tests can substitute them. */
export const catalogApi = {
  async getGames(): Promise<GameSummary[]> {
    const result = await api.GET('/catalog/games');
    if (!result.data) throw new Error(`catalog request failed (${result.response.status})`);
    return result.data.flatMap((g) => toGameSummary(g) ?? []);
  },
};

/** Categories present in the catalog, in a fixed order, with how many games each has. */
export function categoriesOf(games: GameSummary[]): CategorySummary[] {
  return (Object.keys(categoryLabels) as Category[])
    .map((category) => ({ category, label: categoryLabels[category], gameCount: games.filter((g) => g.category === category).length }))
    .filter((c) => c.gameCount > 0);
}

/**
 * Home from the real catalog: the first playable game is featured, coming-soon games get their own row.
 * TODO(gameverse): Continue playing comes from GET /sessions/me once the web has sign-in (Prompt 1.7); quests from
 * Progression (2.1); friends from Social (2.3). Until then those three stay mock, and point only at playable games.
 */
export function buildHomeFeed(games: GameSummary[]): HomeFeed {
  const playable = games.filter((g) => g.playable);
  const featured = playable[0];
  return {
    featured,
    comingSoon: games.filter((g) => !g.playable),
    continuePlaying: featured ? [{ game: featured, status: 'vs AI, level 2', yourTurn: true }] : [],
    quests: structuredClone(mockQuests),
    questsResetInMinutes: mockQuestsResetInMinutes,
    categories: categoriesOf(games),
    friends: structuredClone(mockFriends),
  };
}
