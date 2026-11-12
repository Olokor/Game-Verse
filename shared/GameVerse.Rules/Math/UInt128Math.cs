namespace GameVerse.Rules.Math
{
    /// <summary>
    /// Minimal unsigned 128-bit helpers. netstandard2.1 has no Int128 and no 64x64 Math.BigMul,
    /// so the fixed-point type uses these for exact multiply and divide.
    /// </summary>
    internal static class UInt128Math
    {
        /// <summary>Full 64x64 -> 128 bit unsigned multiply.</summary>
        public static void Multiply(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            ulong aLo = a & 0xFFFFFFFFUL, aHi = a >> 32;
            ulong bLo = b & 0xFFFFFFFFUL, bHi = b >> 32;

            ulong ll = aLo * bLo;
            ulong lh = aLo * bHi;
            ulong hl = aHi * bLo;
            ulong hh = aHi * bHi;

            ulong mid = (ll >> 32) + (lh & 0xFFFFFFFFUL) + (hl & 0xFFFFFFFFUL);
            lo = (mid << 32) | (ll & 0xFFFFFFFFUL);
            hi = hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }

        /// <summary>
        /// Divides the 128-bit value (hi:lo) by d. Requires hi &lt; d so the quotient fits in 64 bits;
        /// callers check that first and report overflow.
        /// </summary>
        public static ulong Divide(ulong hi, ulong lo, ulong d, out ulong remainder)
        {
            ulong rem = hi;
            ulong quotient = 0;
            for (int i = 63; i >= 0; i--)
            {
                bool carry = (rem >> 63) != 0;
                rem = (rem << 1) | ((lo >> i) & 1UL);
                if (carry || rem >= d)
                {
                    rem -= d;
                    quotient |= 1UL << i;
                }
            }

            remainder = rem;
            return quotient;
        }

        /// <summary>Compares (aHi:aLo) with (bHi:bLo).</summary>
        public static int Compare(ulong aHi, ulong aLo, ulong bHi, ulong bLo)
        {
            if (aHi != bHi)
            {
                return aHi < bHi ? -1 : 1;
            }

            return aLo == bLo ? 0 : (aLo < bLo ? -1 : 1);
        }
    }
}
