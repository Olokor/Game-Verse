import { cn } from '../../lib/cn';
import { formatInt } from '../../lib/format';
import { CoinIcon, GemIcon } from '../icons/CurrencyIcons';
import { Skeleton } from './Skeleton';
import { useCountUp } from './useCountUp';

export type Currency = 'coin' | 'gem';

const meta: Record<Currency, { icon: typeof CoinIcon; color: string; singular: string; plural: string }> = {
  coin: { icon: CoinIcon, color: 'text-currency-coin', singular: 'coin', plural: 'coins' },
  gem: { icon: GemIcon, color: 'text-currency-gem', singular: 'gem', plural: 'gems' },
};

export interface CurrencyPillProps {
  currency: Currency;
  amount: number;
  /** Optional: makes the pill a button (e.g. opens the store). */
  onClick?: () => void;
  loading?: boolean;
  className?: string;
}

/**
 * Currency icon + tabular amount. Counts up when the amount changes, with a brief highlight on gains.
 * The accessible name always states the final amount, not the animating one.
 */
export function CurrencyPill({ currency, amount, onClick, loading = false, className }: CurrencyPillProps) {
  const { value, animating } = useCountUp(amount);
  // Highlight only while counting up toward a higher amount. Derived, so it can never get stuck
  // (with reduced motion there is no animation and therefore no highlight).
  const gain = animating && value < amount;
  const { icon: Icon, color, singular, plural } = meta[currency];

  if (loading) {
    return <Skeleton className={cn('h-32 w-72 rounded-pill', className)} />;
  }

  const label = `${formatInt(amount)} ${amount === 1 ? singular : plural}`;
  const body = (
    <>
      <Icon className={cn('size-20', color)} />
      <span className={cn('type-numeric text-body-sm font-bold transition-colors', gain ? 'text-state-success' : 'text-text-primary')}>
        {formatInt(value)}
      </span>
    </>
  );
  const base = 'inline-flex h-32 items-center gap-8 rounded-pill border border-border-subtle bg-bg-surface pl-8 pr-12';

  if (onClick) {
    return (
      // The visible pill is 32px; the hit area is padded out to the 44px touch target.
      <button type="button" onClick={onClick} aria-label={label} className={cn('pressable group inline-flex min-h-touch-target items-center', className)}>
        <span className={cn(base, 'group-hover:border-border-strong')}>{body}</span>
      </button>
    );
  }
  return (
    <span role="img" aria-label={label} className={cn(base, className)}>
      {body}
    </span>
  );
}
