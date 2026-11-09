/**
 * Reads motion tokens from the CSS variables in tokens.css, so JS animations (count-ups, bars) use the
 * same values as CSS transitions without copying numbers into code. When the stylesheet is not loaded
 * (unit tests), durations read as 0 and animations complete immediately.
 */

type Easing = [number, number, number, number];

const KEYWORD_EASINGS: Record<string, Easing> = {
  linear: [0, 0, 1, 1],
  ease: [0.25, 0.1, 0.25, 1],
  'ease-in': [0.42, 0, 1, 1],
  'ease-out': [0, 0, 0.58, 1],
  'ease-in-out': [0.42, 0, 0.58, 1],
};

function readVar(name: string): string {
  if (typeof document === 'undefined') return '';
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

/** Duration token in seconds, e.g. durationToken('slow') reads --gv-motion-slow-duration. */
export function durationToken(name: 'instant' | 'fast' | 'base' | 'slow'): number {
  const raw = readVar(`--gv-motion-${name}-duration`);
  const ms = raw.endsWith('ms') ? Number.parseFloat(raw) : Number.NaN;
  return Number.isFinite(ms) ? ms / 1000 : 0;
}

/**
 * motion.slow spring for Motion's transition prop. Falls back to an instant transition when the tokens are unavailable.
 */
export function slowSpring(): { type: 'spring'; stiffness: number; damping: number } | { duration: 0 } {
  const stiffness = Number.parseFloat(readVar('--gv-motion-slow-spring-stiffness'));
  const damping = Number.parseFloat(readVar('--gv-motion-slow-spring-damping'));
  if (!Number.isFinite(stiffness) || !Number.isFinite(damping)) return { duration: 0 };
  return { type: 'spring', stiffness, damping };
}

/** Easing token as cubic-bezier control points, e.g. easingToken('fast'). Linear when unavailable. */
export function easingToken(name: 'instant' | 'fast' | 'base'): Easing {
  const raw = readVar(`--gv-motion-${name}-easing`);
  const keyword = KEYWORD_EASINGS[raw];
  if (keyword) return keyword;
  const match = /^cubic-bezier\(([^)]+)\)$/.exec(raw);
  const points = match?.[1]?.split(',').map((n) => Number.parseFloat(n));
  if (points && points.length === 4 && points.every(Number.isFinite)) return points as Easing;
  return [0, 0, 1, 1];
}
