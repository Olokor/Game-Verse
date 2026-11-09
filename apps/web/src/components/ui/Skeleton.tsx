import type { HTMLAttributes } from 'react';
import { cn } from '../../lib/cn';

/**
 * Loading placeholder block. Screens show skeletons, never spinners (CLAUDE.md section 6).
 * Pulses gently; static under reduced motion. Hidden from assistive tech; wrap groups in an aria-busy region.
 */
export function Skeleton({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div aria-hidden="true" className={cn('rounded-sm bg-bg-elevated skeleton-pulse', className)} {...rest} />;
}

/** A few lines of text, last line shorter. */
export function SkeletonText({ lines = 2, className }: { lines?: number; className?: string }) {
  return (
    <div className={cn('flex flex-col gap-8', className)} aria-hidden="true">
      {Array.from({ length: lines }, (_, i) => (
        <Skeleton key={i} className={cn('h-12', i === lines - 1 && lines > 1 ? 'w-3/5' : 'w-full')} />
      ))}
    </div>
  );
}
