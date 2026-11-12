import { useId } from 'react';
import { Link } from 'react-router';
import { ChevronRight, Hourglass, Play } from 'lucide-react';
import { cn } from '../../lib/cn';
import { formatCompact } from '../../lib/format';
import type { GameSummary } from '../../data/types';
import { buttonClasses } from './Button';
import { GameArt } from './GameArt';
import { Skeleton } from './Skeleton';
import { accentStyle, categoryLabel, categoryStyle } from './tokens';

export type GameCardVariant = 'hero' | 'standard' | 'compact';

export interface GameCardProps {
  game: GameSummary;
  variant?: GameCardVariant;
  /** Secondary line under the title (standard and compact), e.g. "Rated, 5+0, move 23". Defaults to the tagline. */
  subtitle?: string;
  /** It is the player's turn in this game: shows a "Your turn" chip (icon + text). */
  yourTurn?: boolean;
  className?: string;
}

function CategoryChip({ game }: { game: GameSummary }) {
  return (
    <span className="inline-flex items-center gap-4 rounded-pill bg-bg-base/80 px-8 py-4 type-label text-text-primary">
      <span className={cn('size-8 rounded-pill', categoryStyle[game.category].bg)} aria-hidden="true" />
      {categoryLabel[game.category]}
    </span>
  );
}

function Badge({ kind }: { kind: NonNullable<GameSummary['badge']> }) {
  return kind === 'new' ? (
    <span className="rounded-pill bg-text-primary px-8 py-4 type-label text-brand-volt-ink">New</span>
  ) : (
    <span className="rounded-pill bg-accent-flare px-8 py-4 type-label text-brand-volt-ink">Event</span>
  );
}

function LiveCount({ count }: { count: number }) {
  return (
    <span className="inline-flex items-center gap-8 text-body-sm text-text-secondary">
      <span className="relative flex size-8" aria-hidden="true">
        <span className="absolute inset-0 rounded-pill bg-accent-flare live-ping" />
        <span className="relative size-8 rounded-pill bg-accent-flare" />
      </span>
      <span className="type-numeric">{formatCompact(count)} playing</span>
    </span>
  );
}

function YourTurn() {
  return (
    <span className="inline-flex items-center gap-4 rounded-pill bg-accent-sky px-8 py-4 type-label text-brand-volt-ink">
      <Hourglass className="size-12" aria-hidden="true" />
      Your turn
    </span>
  );
}

const cardFrame = 'group pressable block overflow-hidden border border-border-subtle bg-bg-surface hover:border-border-strong';

/**
 * Game card. hero: wide, featured, one soft glow. standard: portrait 3:4. compact: row.
 * The whole card is one link; "Play" inside is a styled label, not a nested button.
 * TODO(gameverse): /games/:id is a placeholder until the game lobby (Prompt 1.6/1.7).
 */
export function GameCard({ game, variant = 'standard', subtitle, yourTurn = false, className }: GameCardProps) {
  const to = `/games/${game.id}`;
  const descriptionId = useId();

  if (variant === 'hero') {
    return (
      <Link
        to={to}
        className={cn(cardFrame, 'featured-glow relative rounded-lg', className)}
        style={accentStyle(game.category)}
        aria-label={`Play ${game.title}`}
        aria-describedby={descriptionId}
      >
        <GameArt category={game.category} title={game.title} size="hero" decorative className="aspect-4/5 sm:aspect-video" />
        <div className="absolute inset-0 flex flex-col justify-between p-16 sm:p-24">
          <div className="flex items-center gap-8">
            <CategoryChip game={game} />
            {game.badge && <Badge kind={game.badge} />}
          </div>
          <div className="flex flex-col gap-12">
            <div className="flex flex-col gap-4">
              <span className="type-label text-text-secondary">Featured</span>
              <h2 className="type-display text-display font-bold sm:text-hero-number">{game.title}</h2>
              <p id={descriptionId} className="max-w-sm text-body text-text-secondary">
                {game.tagline}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-16">
              <span className={buttonClasses({ variant: 'primary', size: 'md' })}>
                <Play className="size-20 fill-current" aria-hidden="true" />
                Play now
              </span>
              <LiveCount count={game.livePlayers} />
            </div>
          </div>
        </div>
      </Link>
    );
  }

  if (variant === 'compact') {
    return (
      <Link to={to} className={cn(cardFrame, 'flex min-h-72 items-center gap-12 rounded-md p-8 pr-12', className)}>
        <GameArt category={game.category} title={game.title} size="icon" decorative className="size-56 shrink-0 rounded-sm" />
        <div className="flex min-w-0 flex-1 flex-col gap-4">
          <span className="flex items-center gap-8">
            <span className="truncate text-body font-bold text-text-primary">{game.title}</span>
            {yourTurn ? <YourTurn /> : game.badge && <Badge kind={game.badge} />}
          </span>
          <span className="line-clamp-2 text-body-sm text-text-secondary">{subtitle ?? game.tagline}</span>
        </div>
        <ChevronRight className="size-20 shrink-0 text-text-muted transition-colors group-hover:text-text-primary" aria-hidden="true" />
      </Link>
    );
  }

  return (
    <Link to={to} className={cn(cardFrame, 'flex flex-col rounded-lg', className)}>
      <div className="relative">
        <GameArt category={game.category} title={game.title} decorative className="aspect-3/4" />
        <div className="absolute inset-x-0 top-0 flex items-center justify-between gap-4 p-12">
          <CategoryChip game={game} />
          {game.badge && <Badge kind={game.badge} />}
        </div>
        {yourTurn && (
          <div className="absolute inset-x-0 bottom-0 p-12">
            <YourTurn />
          </div>
        )}
      </div>
      <div className="flex flex-col gap-4 p-12">
        <span className="type-display truncate text-title-sm font-bold">{game.title}</span>
        <span className="line-clamp-2 text-body-sm text-text-secondary">{subtitle ?? game.tagline}</span>
      </div>
    </Link>
  );
}

export function GameCardSkeleton({ variant = 'standard', className }: { variant?: GameCardVariant; className?: string }) {
  if (variant === 'hero') {
    return <Skeleton className={cn('aspect-4/5 rounded-lg sm:aspect-video', className)} />;
  }
  if (variant === 'compact') {
    return (
      <div className={cn('flex min-h-72 items-center gap-12 rounded-md border border-border-subtle bg-bg-surface p-8', className)} aria-hidden="true">
        <Skeleton className="size-56 rounded-sm" />
        <div className="flex flex-1 flex-col gap-8">
          <Skeleton className="h-12 w-1/2" />
          <Skeleton className="h-12 w-3/4" />
        </div>
      </div>
    );
  }
  return (
    <div className={cn('flex flex-col overflow-hidden rounded-lg border border-border-subtle bg-bg-surface', className)} aria-hidden="true">
      <Skeleton className="aspect-3/4 rounded-none" />
      <div className="flex flex-col gap-8 p-12">
        <Skeleton className="h-16 w-2/3" />
        <Skeleton className="h-12 w-full" />
      </div>
    </div>
  );
}
