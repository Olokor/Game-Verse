import { create } from 'zustand';
import { mockPlayer } from '../data/mock/homeFeed';

/**
 * Local player state shown in the shell (currency, level). Client state lives in Zustand; server data in TanStack Query.
 * TODO(gameverse): seeded from mock data; hydrate from the profile and wallet endpoints once they exist (Prompts 1.1, 2.2).
 */
interface PlayerState {
  displayName: string;
  rank: typeof mockPlayer.rank;
  level: number;
  xp: number;
  xpRequired: number;
  coins: number;
  gems: number;
  addCoins: (amount: number) => void;
  addXp: (amount: number) => void;
}

export const usePlayerStore = create<PlayerState>()((set) => ({
  ...mockPlayer,
  addCoins: (amount) => set((s) => ({ coins: Math.max(0, s.coins + amount) })),
  addXp: (amount) =>
    set((s) => {
      let xp = s.xp + amount;
      let level = s.level;
      while (xp >= s.xpRequired) {
        xp -= s.xpRequired;
        level += 1;
      }
      return { xp, level };
    }),
}));
