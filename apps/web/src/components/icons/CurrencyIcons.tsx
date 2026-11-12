import type { SVGProps } from 'react';

// Custom currency marks. They draw with currentColor; callers set text-currency-coin / text-currency-gem.

export function CoinIcon(props: SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true" {...props}>
      <circle cx="12" cy="12" r="10" fill="currentColor" />
      <circle cx="12" cy="12" r="6.5" stroke="var(--gv-color-bg-base)" strokeOpacity="0.35" strokeWidth="1.5" />
      <path d="M12 8.5v7M9.5 10.5 12 8.5l2.5 2" stroke="var(--gv-color-bg-base)" strokeOpacity="0.55" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function GemIcon(props: SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true" {...props}>
      <path d="M7 3.5h10l4 5.5-9 11.5L3 9z" fill="currentColor" />
      <path d="M3 9h18M9.5 3.5 8 9l4 11.5L16 9l-1.5-5.5" stroke="var(--gv-color-bg-base)" strokeOpacity="0.35" strokeWidth="1.2" strokeLinejoin="round" />
    </svg>
  );
}
