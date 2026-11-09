import { m, useReducedMotion } from 'motion/react';
import { cn } from '../../lib/cn';
import { formatInt } from '../../lib/format';
import { slowSpring } from '../../lib/motionTokens';
import { Skeleton } from './Skeleton';
import { useCountUp } from './useCountUp';

export interface XPBarProps {
  level: number;
  xp: number;
  xpRequired: number;
  /** compact: level badge + bar only, for headers. */
  compact?: boolean;
  loading?: boolean;
  className?: string;
}

/** Level number, progress toward the next level, and an XP count that counts up. */
export function XPBar({ level, xp, xpRequired, compact = false, loading = false, className }: XPBarProps) {
  const reduced = useReducedMotion();
  const shown = useCountUp(xp).value;
  const ratio = xpRequired > 0 ? Math.min(1, Math.max(0, xp / xpRequired)) : 0;

  if (loading) {
    return (
      <div className={cn('flex items-center gap-12', className)} aria-hidden="true">
        <Skeleton className="size-32 rounded-sm" />
        <div className="flex flex-1 flex-col gap-8">
          {!compact && <Skeleton className="h-12 w-1/3" />}
          <Skeleton className="h-8 w-full rounded-pill" />
        </div>
      </div>
    );
  }

  return (
    <div className={cn('flex items-center gap-12', className)}>
      <span
        className="type-display type-numeric flex size-32 shrink-0 items-center justify-center rounded-sm bg-brand-volt text-body-sm font-bold text-brand-volt-ink"
        aria-hidden="true"
      >
        {level}
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-4">
        {!compact && (
          <div className="flex items-baseline justify-between gap-8 text-body-sm">
            <span className="font-bold text-text-primary">Level {level}</span>
            <span className="type-numeric text-text-secondary">
              {formatInt(shown)} / {formatInt(xpRequired)} XP
            </span>
          </div>
        )}
        <div
          role="progressbar"
          aria-label={`Level ${level} progress`}
          aria-valuemin={0}
          aria-valuemax={xpRequired}
          aria-valuenow={xp}
          aria-valuetext={`${formatInt(xp)} of ${formatInt(xpRequired)} XP`}
          className="h-8 overflow-hidden rounded-pill bg-border-subtle"
        >
          <m.div
            className="h-full origin-left rounded-pill bg-brand-volt"
            initial={false}
            animate={{ scaleX: ratio }}
            transition={reduced ? { duration: 0 } : slowSpring()}
          />
        </div>
      </div>
    </div>
  );
}
