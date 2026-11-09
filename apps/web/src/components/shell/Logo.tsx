import { cn } from '../../lib/cn';

/** Brand mark: volt tile with a "G", optionally followed by the wordmark. TODO(gameverse): final logo from brand work. */
export function Logo({ wordmark = true, className }: { wordmark?: boolean; className?: string }) {
  return (
    <span className={cn('inline-flex items-center gap-8', className)}>
      <span
        className="type-display flex size-32 items-center justify-center rounded-sm bg-brand-volt text-title-sm font-bold text-brand-volt-ink"
        aria-hidden="true"
      >
        G
      </span>
      {wordmark ? (
        <span className="type-display text-title font-bold text-text-primary">GameVerse</span>
      ) : (
        <span className="sr-only">GameVerse</span>
      )}
    </span>
  );
}
