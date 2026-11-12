import { useQuery } from '@tanstack/react-query';
import type { HomeFeed } from './types';
import { mockHomeFeed } from './mock/homeFeed';

// TODO(gameverse): mock latency so loading skeletons are visible in development. Remove with the mocks.
const MOCK_LATENCY_MS = import.meta.env.MODE === 'test' ? 0 : 450;

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms));

/**
 * Data access for the shell. Hooks call through this object so tests can substitute a call.
 * TODO(gameverse): replace the mock with the real home feed call (openapi-fetch client from packages/api-schema).
 */
export const homeApi = {
  async getHomeFeed(): Promise<HomeFeed> {
    await delay(MOCK_LATENCY_MS);
    return structuredClone(mockHomeFeed);
  },
};

export const homeFeedKey = ['home-feed'] as const;

export function useHomeFeed() {
  return useQuery({ queryKey: homeFeedKey, queryFn: () => homeApi.getHomeFeed() });
}
