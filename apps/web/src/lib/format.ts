// TODO(gameverse): use the player's locale once localization lands; fixed to 'en' so output is stable.
const integer = new Intl.NumberFormat('en', { maximumFractionDigits: 0 });
const compact = new Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 });

export const formatInt = (n: number): string => integer.format(Math.round(n));

/** 1234 -> "1.2K", for live player counts. */
export const formatCompact = (n: number): string => compact.format(n);
