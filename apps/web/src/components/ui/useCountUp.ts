import { useEffect, useRef, useState } from 'react';
import { useReducedMotion } from 'motion/react';
import { cubicBezier } from '../../lib/cubicBezier';
import { durationToken, easingToken } from '../../lib/motionTokens';

/**
 * Numbers that change count up, never jump (DESIGN_SYSTEM.md section 5). A requestAnimationFrame loop rather than
 * Motion's animate(), which would pull Motion's whole animation engine into the initial bundle.
 * Returns the value to display and whether it is mid-animation. Reduced motion shows the target at once.
 */
export function useCountUp(target: number): { value: number; animating: boolean } {
  const reduced = useReducedMotion();
  const [value, setValue] = useState(target);
  const [animating, setAnimating] = useState(false);
  const shown = useRef(target);

  useEffect(() => {
    const from = shown.current;
    if (from === target) return;
    const durationMs = durationToken('slow') * 1000;
    if (reduced || durationMs === 0) {
      shown.current = target;
      setValue(target);
      return;
    }

    const ease = cubicBezier(...easingToken('base'));
    let frame = 0;
    let start: number | undefined;
    setAnimating(true);

    const step = (now: number) => {
      start ??= now;
      const t = Math.min(1, (now - start) / durationMs);
      const v = from + (target - from) * ease(t);
      shown.current = v;
      setValue(v);
      if (t < 1) frame = requestAnimationFrame(step);
      else setAnimating(false);
    };
    frame = requestAnimationFrame(step);

    return () => {
      cancelAnimationFrame(frame);
      setAnimating(false);
    };
  }, [target, reduced]);

  return { value, animating };
}
