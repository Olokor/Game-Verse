import type { ReactNode } from 'react';
import { cn } from '../../lib/cn';
import type { Presence, RankTier } from '../../data/types';
import { Skeleton } from './Skeleton';
import { rankStyle } from './tokens';

const presenceStyle: Record<Presence, { dot: string; label: string }> = {
  online: { dot: 'bg-state-success', label: 'Online' },
  'in-game': { dot: 'bg-accent-sky', label: 'In game' },
  offline: { dot: 'bg-text-muted', label: 'Offline' },
};

export interface PlayerChipProps {
  displayName: string;
  rank: RankTier;
  presence?: Presence;
  /** Shown after "In game", e.g. the game title. */
  activity?: string;
  /** Hide the status line, e.g. in the header. */
  hideStatus?: boolean;
  /** Trailing slot, e.g. an Invite button. */
  action?: ReactNode;
  className?: string;
}

/** Avatar + rank frame + name + rank badge + presence. Presence and rank both carry text, never color alone. */
export function PlayerChip({ displayName, rank, presence = 'online', activity, hideStatus = false, action, className }: PlayerChipProps) {
  const r = rankStyle[rank];
  const p = presenceStyle[presence];
  const RankIcon = r.icon;
  const status = presence === 'in-game' && activity ? `In ${activity}` : p.label;

  return (
    <div className={cn('flex min-w-0 items-center gap-12', className)}>
      <span className="relative shrink-0">
        {/* TODO(gameverse): avatar art and cosmetic frames arrive with Profiles (Phase 2); initials until then. */}
        <span
          className={cn(
            'flex size-40 items-center justify-center rounded-pill bg-bg-elevated text-body-sm font-bold text-text-primary ring-2 ring-offset-2 ring-offset-bg-surface',
            r.ring,
            presence === 'offline' && 'opacity-60',
          )}
          aria-hidden="true"
        >
          {displayName.slice(0, 1).toUpperCase()}
        </span>
        {!hideStatus && (
          <span className={cn('absolute -right-4 -bottom-4 size-12 rounded-pill border-2 border-bg-surface', p.dot)} aria-hidden="true" />
        )}
      </span>
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="flex min-w-0 items-center gap-8">
          <span className="truncate text-body-sm font-bold text-text-primary">{displayName}</span>
          <span className={cn('inline-flex shrink-0 items-center gap-4 type-label', r.text)}>
            <RankIcon className="size-12" aria-hidden="true" />
            {r.label}
          </span>
        </span>
        {!hideStatus && <span className="truncate text-body-sm text-text-secondary">{status}</span>}
      </span>
      {action}
    </div>
  );
}

export function PlayerChipSkeleton() {
  return (
    <div className="flex items-center gap-12" aria-hidden="true">
      <Skeleton className="size-40 rounded-pill" />
      <div className="flex flex-1 flex-col gap-8">
        <Skeleton className="h-12 w-1/2" />
        <Skeleton className="h-12 w-1/3" />
      </div>
    </div>
  );
}
