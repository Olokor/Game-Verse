import type { CSSProperties } from 'react';
import {
  Crown, Dice5, Flame, Gamepad2, Medal, Mountain, Puzzle, Shield, ShieldHalf, Sparkles, Spade, Swords, Trophy, Zap,
  type LucideIcon,
} from 'lucide-react';
import type { Category, RankTier } from '../../data/types';

/**
 * Category and rank colors as token references. Full class strings are written out so Tailwind can see them.
 * Never build a class name from a variable (e.g. `text-category-${c}`): Tailwind would not generate it.
 */
export const categoryStyle: Record<Category, { text: string; bg: string; icon: LucideIcon; accentVar: string }> = {
  board: { text: 'text-category-board', bg: 'bg-category-board', icon: Dice5, accentVar: 'var(--gv-color-category-board)' },
  cards: { text: 'text-category-cards', bg: 'bg-category-cards', icon: Spade, accentVar: 'var(--gv-color-category-cards)' },
  arcade: { text: 'text-category-arcade', bg: 'bg-category-arcade', icon: Zap, accentVar: 'var(--gv-color-category-arcade)' },
  racing: { text: 'text-category-racing', bg: 'bg-category-racing', icon: Mountain, accentVar: 'var(--gv-color-category-racing)' },
  fight: { text: 'text-category-fight', bg: 'bg-category-fight', icon: Swords, accentVar: 'var(--gv-color-category-fight)' },
  adventure: { text: 'text-category-adventure', bg: 'bg-category-adventure', icon: Sparkles, accentVar: 'var(--gv-color-category-adventure)' },
  brain: { text: 'text-category-brain', bg: 'bg-category-brain', icon: Puzzle, accentVar: 'var(--gv-color-category-brain)' },
};

export const categoryLabel: Record<Category, string> = {
  board: 'Board', cards: 'Cards', arcade: 'Arcade', racing: 'Racing', fight: 'Fight', adventure: 'Adventure', brain: 'Brain',
};

/** Sets --art-accent for the art-glow and featured-glow utilities. */
export const accentStyle = (category: Category): CSSProperties =>
  ({ '--art-accent': categoryStyle[category].accentVar }) as CSSProperties;

/** Rank badges always pair the color with an icon and a name, so color is never the only signal. */
export const rankStyle: Record<RankTier, { label: string; text: string; ring: string; icon: LucideIcon }> = {
  bronze: { label: 'Bronze', text: 'text-rank-bronze', ring: 'ring-rank-bronze', icon: Shield },
  silver: { label: 'Silver', text: 'text-rank-silver', ring: 'ring-rank-silver', icon: ShieldHalf },
  gold: { label: 'Gold', text: 'text-rank-gold', ring: 'ring-rank-gold', icon: Medal },
  platinum: { label: 'Platinum', text: 'text-rank-platinum', ring: 'ring-rank-platinum', icon: Trophy },
  diamond: { label: 'Diamond', text: 'text-rank-diamond', ring: 'ring-rank-diamond', icon: Flame },
  legend: { label: 'Legend', text: 'text-rank-legend', ring: 'ring-rank-legend', icon: Crown },
};

export const FallbackGameIcon = Gamepad2;
