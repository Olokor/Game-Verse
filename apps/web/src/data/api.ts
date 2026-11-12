import { useQuery } from '@tanstack/react-query';
import type { HomeFeed } from './types';
import { buildHomeFeed, catalogApi } from './catalog';

/** Data access for the shell. Hooks call through this object so tests can substitute a call. */
export const homeApi = {
  /** Home from the real catalog (GET /catalog/games); quests and friends are still mock (src/data/catalog.ts). */
  async getHomeFeed(): Promise<HomeFeed> {
    return buildHomeFeed(await catalogApi.getGames());
  },
};

export const homeFeedKey = ['home-feed'] as const;

export function useHomeFeed() {
  return useQuery({ queryKey: homeFeedKey, queryFn: () => homeApi.getHomeFeed() });
}

export const catalogKey = ['catalog'] as const;

export function useCatalog() {
  return useQuery({ queryKey: catalogKey, queryFn: () => catalogApi.getGames() });
}
