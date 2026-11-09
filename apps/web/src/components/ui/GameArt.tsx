import { cn } from '../../lib/cn';
import type { Category } from '../../data/types';
import { accentStyle, categoryStyle } from './tokens';

/**
 * Placeholder key art: dark backdrop, one soft glow in the category color, a bold silhouette (the category icon).
 * TODO(gameverse): replace with commissioned key art per game (hero 16:9, card 3:4, icon 1:1), DESIGN_SYSTEM.md section 8.
 */
export function GameArt({ category, title, className, size = 'card', decorative = false }: {
  category: Category;
  title: string;
  className?: string;
  size?: 'hero' | 'card' | 'icon';
  /** Hide from assistive tech when the title is already visible next to the art (every game card). */
  decorative?: boolean;
}) {
  const Icon = categoryStyle[category].icon;
  return (
    <div className={cn('art-glow relative overflow-hidden', className)} style={accentStyle(category)}
      {...(decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': `${title} artwork` })}
    >
      <Icon
        aria-hidden="true"
        strokeWidth={1.25}
        className={cn(
          'absolute opacity-90',
          categoryStyle[category].text,
          size === 'hero' && '-right-8 -top-8 size-1/2 rotate-12 sm:right-24 sm:size-2/5',
          size === 'card' && '-right-12 top-12 size-3/4 rotate-12',
          size === 'icon' && 'inset-0 m-auto size-3/5',
        )}
      />
    </div>
  );
}
