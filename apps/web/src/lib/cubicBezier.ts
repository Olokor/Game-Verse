/**
 * CSS cubic-bezier(x1, y1, x2, y2) as a function of progress t in [0, 1], for JS animations that should
 * match the CSS easing tokens. Newton-Raphson with a bisection fallback, the same approach browsers use.
 */
export function cubicBezier(x1: number, y1: number, x2: number, y2: number): (t: number) => number {
  const cx = 3 * x1;
  const bx = 3 * (x2 - x1) - cx;
  const ax = 1 - cx - bx;
  const cy = 3 * y1;
  const by = 3 * (y2 - y1) - cy;
  const ay = 1 - cy - by;

  const sampleX = (s: number) => ((ax * s + bx) * s + cx) * s;
  const sampleY = (s: number) => ((ay * s + by) * s + cy) * s;
  const slopeX = (s: number) => (3 * ax * s + 2 * bx) * s + cx;

  const solveX = (x: number) => {
    let s = x;
    for (let i = 0; i < 8; i++) {
      const err = sampleX(s) - x;
      if (Math.abs(err) < 1e-6) return s;
      const d = slopeX(s);
      if (Math.abs(d) < 1e-6) break;
      s -= err / d;
    }
    let lo = 0;
    let hi = 1;
    s = x;
    while (lo < hi) {
      const v = sampleX(s);
      if (Math.abs(v - x) < 1e-6) return s;
      if (x > v) lo = s;
      else hi = s;
      s = (hi - lo) / 2 + lo;
      if (hi - lo < 1e-7) break;
    }
    return s;
  };

  return (t) => (t <= 0 ? 0 : t >= 1 ? 1 : sampleY(solveX(t)));
}
