import { AlertTriangle, Check } from 'lucide-react';
import { cn } from '../../lib/cn';
import { formatInt } from '../../lib/format';
import type { Quest } from '../../data/types';
import { CoinIcon } from '../icons/CurrencyIcons';
import { Button } from './Button';
import { Skeleton } from './Skeleton';

export type QuestState = 'active' | 'claimable' | 'claimed';

export const questState = (q: Quest): QuestState => (q.claimed ? 'claimed' : q.progress >= q.goal ? 'claimable' : 'active');

export interface QuestCardProps {
  quest: Quest;
  onClaim?: (id: string) => void;
  claiming?: boolean;
  /** The last claim attempt failed; shows an inline message and keeps Claim available. */
  error?: boolean;
  className?: string;
}

/** Daily quest: title, progress, reward preview, claim state (active, claimable, claimed). */
export function QuestCard({ quest, onClaim, claiming = false, error = false, className }: QuestCardProps) {
  const state = questState(quest);
  const ratio = Math.min(1, quest.progress / quest.goal);

  return (
    <div
      className={cn(
        'flex items-center gap-12 rounded-md border bg-bg-surface p-12',
        state === 'claimable' ? 'border-brand-volt' : 'border-border-subtle',
        className,
      )}
    >
      <div className="flex min-w-0 flex-1 flex-col gap-8">
        <span className={cn('text-body-sm font-bold', state === 'claimed' ? 'text-text-secondary line-through' : 'text-text-primary')}>
          {quest.title}
        </span>
        <div className="flex items-center gap-8">
          <div
            role="progressbar"
            aria-label={`${quest.title} progress`}
            aria-valuemin={0}
            aria-valuemax={quest.goal}
            aria-valuenow={Math.min(quest.progress, quest.goal)}
            className="h-4 flex-1 overflow-hidden rounded-pill bg-border-subtle"
          >
            <div
              className={cn('h-full origin-left rounded-pill', state === 'claimed' ? 'bg-text-muted' : 'bg-brand-volt')}
              style={{ transform: `scaleX(${ratio})` }}
            />
          </div>
          <span className="type-numeric shrink-0 text-body-sm text-text-secondary">
            {formatInt(Math.min(quest.progress, quest.goal))}/{formatInt(quest.goal)}
          </span>
        </div>
        {error && state === 'claimable' && (
          <span role="alert" className="inline-flex items-center gap-4 text-body-sm text-state-danger">
            <AlertTriangle className="size-16 shrink-0" aria-hidden="true" />
            Couldn't claim. Try again.
          </span>
        )}
      </div>

      <div className="flex shrink-0 flex-col items-end gap-4">
        {state === 'claimable' ? (
          <Button
            variant="secondary"
            size="sm"
            pill
            loading={claiming}
            onClick={() => onClaim?.(quest.id)}
            aria-label={`Claim ${formatInt(quest.reward.amount)} ${quest.reward.kind === 'coin' ? 'coins' : 'XP'} for ${quest.title}`}
          >
            Claim
          </Button>
        ) : state === 'claimed' ? (
          <span className="inline-flex h-touch-target items-center gap-4 text-body-sm font-bold text-state-success">
            <Check className="size-16" aria-hidden="true" />
            Claimed
          </span>
        ) : null}
        {state !== 'claimed' && <Reward reward={quest.reward} />}
      </div>
    </div>
  );
}

function Reward({ reward }: { reward: Quest['reward'] }) {
  return reward.kind === 'coin' ? (
    <span className="inline-flex items-center gap-4 text-body-sm font-bold text-currency-coin">
      <CoinIcon className="size-16" />
      <span className="type-numeric">+{formatInt(reward.amount)}</span>
      <span className="sr-only">coins</span>
    </span>
  ) : (
    <span className="type-numeric text-body-sm font-bold text-brand-volt">+{formatInt(reward.amount)} XP</span>
  );
}

export function QuestCardSkeleton() {
  return (
    <div className="flex items-center gap-12 rounded-md border border-border-subtle bg-bg-surface p-12" aria-hidden="true">
      <div className="flex flex-1 flex-col gap-8">
        <Skeleton className="h-12 w-3/4" />
        <Skeleton className="h-4 w-full rounded-pill" />
      </div>
      <Skeleton className="h-16 w-40" />
    </div>
  );
}
