using System;
using System.Globalization;
using System.Numerics;

namespace GameVerse.Rules.Math
{
    /// <summary>
    /// Deterministic Q32.32 fixed-point number: 32 integer bits and 32 fraction bits in a <see cref="long"/>.
    /// Use this for every value in replayed or verified simulation state, never float or double,
    /// because CoreCLR (server) and IL2CPP (Android) can disagree on floating-point results.
    /// <para>
    /// Arithmetic never wraps silently: results that do not fit throw <see cref="OverflowException"/>.
    /// Multiplication, division, ratios, parsing and square root round to the nearest representable
    /// value, with exact halves rounded away from zero.
    /// </para>
    /// </summary>
    public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
    {
        /// <summary>Number of fraction bits.</summary>
        public const int FractionBits = 32;

        private const long OneRaw = 1L << FractionBits;
        private const long FractionMask = OneRaw - 1;
        private const ulong SignBit = 1UL << 63;

        private Fixed(long raw)
        {
            Raw = raw;
        }

        /// <summary>Gets the raw two's-complement Q32.32 bits. Value = Raw / 2^32.</summary>
        public long Raw { get; }

        /// <summary>Gets 0.</summary>
        public static Fixed Zero => new Fixed(0);

        /// <summary>Gets 1.</summary>
        public static Fixed One => new Fixed(OneRaw);

        /// <summary>Gets 0.5.</summary>
        public static Fixed Half => new Fixed(OneRaw >> 1);

        /// <summary>Gets the smallest positive value, 2^-32.</summary>
        public static Fixed Epsilon => new Fixed(1);

        /// <summary>Gets the largest value, just under 2^31.</summary>
        public static Fixed MaxValue => new Fixed(long.MaxValue);

        /// <summary>Gets the smallest value, -2^31.</summary>
        public static Fixed MinValue => new Fixed(long.MinValue);

        /// <summary>Addition. Throws on overflow.</summary>
        public static Fixed operator +(Fixed a, Fixed b) => new Fixed(checked(a.Raw + b.Raw));

        /// <summary>Subtraction. Throws on overflow.</summary>
        public static Fixed operator -(Fixed a, Fixed b) => new Fixed(checked(a.Raw - b.Raw));

        /// <summary>Negation. Throws for <see cref="MinValue"/>.</summary>
        public static Fixed operator -(Fixed a) => new Fixed(checked(-a.Raw));

        /// <summary>Multiplication, rounded to nearest (halves away from zero). Throws on overflow.</summary>
        public static Fixed operator *(Fixed a, Fixed b)
        {
            bool negative = (a.Raw < 0) != (b.Raw < 0);
            UInt128Math.Multiply(Magnitude(a.Raw), Magnitude(b.Raw), out ulong hi, out ulong lo);

            // Add half of the last kept bit, then drop the 32 fraction bits.
            ulong roundedLo = unchecked(lo + (1UL << (FractionBits - 1)));
            if (roundedLo < lo)
            {
                hi++;
            }

            if ((hi >> FractionBits) != 0)
            {
                throw new OverflowException("Fixed multiplication overflow.");
            }

            ulong magnitude = (hi << FractionBits) | (roundedLo >> FractionBits);
            return new Fixed(ApplySign(magnitude, negative));
        }

        /// <summary>Division, rounded to nearest (halves away from zero). Throws on overflow or division by zero.</summary>
        public static Fixed operator /(Fixed a, Fixed b) => new Fixed(DivideScaled(a.Raw, b.Raw));

        /// <summary>Equality.</summary>
        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;

        /// <summary>Inequality.</summary>
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;

        /// <summary>Less than.</summary>
        public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;

        /// <summary>Greater than.</summary>
        public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;

        /// <summary>Less than or equal.</summary>
        public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;

        /// <summary>Greater than or equal.</summary>
        public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

        /// <summary>Wraps raw Q32.32 bits, e.g. from a snapshot or fixture.</summary>
        /// <param name="raw">Raw bits.</param>
        /// <returns>The fixed value.</returns>
        public static Fixed FromRaw(long raw) => new Fixed(raw);

        /// <summary>Converts an integer exactly.</summary>
        /// <param name="value">Integer value.</param>
        /// <returns>The fixed value.</returns>
        public static Fixed FromInt(int value) => new Fixed((long)value << FractionBits);

        /// <summary>Computes numerator / denominator, rounded to nearest (halves away from zero).</summary>
        /// <param name="numerator">Numerator.</param>
        /// <param name="denominator">Denominator, not zero.</param>
        /// <returns>The fixed value.</returns>
        public static Fixed FromRatio(long numerator, long denominator) => new Fixed(DivideScaled(numerator, denominator));

        /// <summary>
        /// Parses an invariant-culture decimal string such as "-12.375" exactly, then rounds to the
        /// nearest representable value. Intended for fixtures and tuning constants.
        /// </summary>
        /// <param name="text">Decimal text.</param>
        /// <returns>The fixed value.</returns>
        public static Fixed Parse(string text)
        {
            if (!TryParse(text, out Fixed value))
            {
                throw new FormatException("Not a valid fixed-point decimal: '" + text + "'.");
            }

            return value;
        }

        /// <summary>Parses like <see cref="Parse"/>; returns false on bad format or out-of-range value.</summary>
        /// <param name="text">Decimal text.</param>
        /// <param name="value">The parsed value, or zero on failure.</param>
        /// <returns>True when parsing succeeded.</returns>
        public static bool TryParse(string? text, out Fixed value)
        {
            value = Zero;
            if (text == null)
            {
                return false;
            }

            string s = text.Trim();
            bool negative = false;
            int start = 0;
            if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
            {
                negative = s[0] == '-';
                start = 1;
            }

            BigInteger digits = BigInteger.Zero;
            BigInteger scale = BigInteger.One;
            bool seenPoint = false;
            bool seenDigit = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '.' && !seenPoint)
                {
                    seenPoint = true;
                    continue;
                }

                if (c < '0' || c > '9')
                {
                    return false;
                }

                seenDigit = true;
                digits = (digits * 10) + (c - '0');
                if (seenPoint)
                {
                    scale *= 10;
                }
            }

            if (!seenDigit)
            {
                return false;
            }

            BigInteger quotient = BigInteger.DivRem(digits << FractionBits, scale, out BigInteger remainder);
            if (remainder * 2 >= scale)
            {
                quotient += 1;
            }

            if (negative)
            {
                quotient = -quotient;
            }

            if (quotient > long.MaxValue || quotient < long.MinValue)
            {
                return false;
            }

            value = new Fixed((long)quotient);
            return true;
        }

        /// <summary>Named alternative to +.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns>The sum.</returns>
        public static Fixed Add(Fixed left, Fixed right) => left + right;

        /// <summary>Named alternative to -.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns>The difference.</returns>
        public static Fixed Subtract(Fixed left, Fixed right) => left - right;

        /// <summary>Named alternative to *.</summary>
        /// <param name="left">Left operand.</param>
        /// <param name="right">Right operand.</param>
        /// <returns>The product.</returns>
        public static Fixed Multiply(Fixed left, Fixed right) => left * right;

        /// <summary>Named alternative to /.</summary>
        /// <param name="left">Dividend.</param>
        /// <param name="right">Divisor.</param>
        /// <returns>The quotient.</returns>
        public static Fixed Divide(Fixed left, Fixed right) => left / right;

        /// <summary>Named alternative to unary -.</summary>
        /// <param name="item">Operand.</param>
        /// <returns>The negated value.</returns>
        public static Fixed Negate(Fixed item) => -item;

        /// <summary>Absolute value. Throws for <see cref="MinValue"/>.</summary>
        /// <param name="a">Value.</param>
        /// <returns>The absolute value.</returns>
        public static Fixed Abs(Fixed a) => a.Raw < 0 ? -a : a;

        /// <summary>Smaller of two values.</summary>
        /// <param name="a">First value.</param>
        /// <param name="b">Second value.</param>
        /// <returns>The smaller value.</returns>
        public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;

        /// <summary>Larger of two values.</summary>
        /// <param name="a">First value.</param>
        /// <param name="b">Second value.</param>
        /// <returns>The larger value.</returns>
        public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;

        /// <summary>Clamps value into [min, max].</summary>
        /// <param name="value">Value.</param>
        /// <param name="min">Lower bound.</param>
        /// <param name="max">Upper bound, not below min.</param>
        /// <returns>The clamped value.</returns>
        public static Fixed Clamp(Fixed value, Fixed min, Fixed max)
        {
            if (min > max)
            {
                throw new ArgumentException("min must not exceed max.", nameof(min));
            }

            return value < min ? min : (value > max ? max : value);
        }

        /// <summary>Largest integer value not greater than a.</summary>
        /// <param name="a">Value.</param>
        /// <returns>The floor.</returns>
        public static Fixed Floor(Fixed a) => new Fixed(a.Raw & ~FractionMask);

        /// <summary>Smallest integer value not less than a. Throws on overflow.</summary>
        /// <param name="a">Value.</param>
        /// <returns>The ceiling.</returns>
        public static Fixed Ceiling(Fixed a)
        {
            Fixed floor = Floor(a);
            return floor.Raw == a.Raw ? floor : floor + One;
        }

        /// <summary>Nearest integer value, halves away from zero. Throws on overflow.</summary>
        /// <param name="a">Value.</param>
        /// <returns>The rounded value.</returns>
        public static Fixed Round(Fixed a)
        {
            if (a.Raw < 0)
            {
                return -Round(-a);
            }

            return Floor(a + Half);
        }

        /// <summary>Square root, rounded to nearest. Throws for negative input.</summary>
        /// <param name="a">Non-negative value.</param>
        /// <returns>The square root.</returns>
        public static Fixed Sqrt(Fixed a)
        {
            if (a.Raw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(a), "Square root of a negative number.");
            }

            // sqrt(Raw / 2^32) * 2^32 = sqrt(Raw * 2^32). Raw * 2^32 < 2^95, so the root is < 2^48.
            ulong targetHi = (ulong)a.Raw >> FractionBits;
            ulong targetLo = (ulong)a.Raw << FractionBits;

            ulong low = 0;
            ulong high = 1UL << 48;
            while (low < high)
            {
                ulong mid = low + ((high - low + 1) >> 1);
                UInt128Math.Multiply(mid, mid, out ulong sqHi, out ulong sqLo);
                if (UInt128Math.Compare(sqHi, sqLo, targetHi, targetLo) <= 0)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            // low = floor(sqrt(target)). Round up when target > low^2 + low, i.e. sqrt >= low + 0.5.
            UInt128Math.Multiply(low, low, out ulong floorHi, out ulong floorLo);
            ulong boundLo = unchecked(floorLo + low);
            ulong boundHi = floorHi + (boundLo < floorLo ? 1UL : 0UL);
            if (UInt128Math.Compare(targetHi, targetLo, boundHi, boundLo) > 0)
            {
                low++;
            }

            return new Fixed((long)low);
        }

        /// <summary>Floor as an int.</summary>
        /// <returns>The integer floor.</returns>
        public int FloorToInt() => (int)(Raw >> FractionBits);

        /// <summary>Round (halves away from zero) as an int.</summary>
        /// <returns>The rounded integer.</returns>
        public int RoundToInt() => Round(this).FloorToInt();

        /// <summary>DISPLAY ONLY. Never feed the result back into simulation state.</summary>
        /// <returns>An approximate double.</returns>
        public double ToDouble() => Raw / (double)OneRaw;

        /// <summary>
        /// Invariant decimal with up to 10 fraction digits, trailing zeros trimmed.
        /// 10 digits is enough for <see cref="Parse"/> to recover the exact same raw value.
        /// </summary>
        /// <returns>The decimal text.</returns>
        public override string ToString()
        {
            BigInteger tenPow10 = BigInteger.Pow(10, 10);
            BigInteger scaled = BigInteger.Abs(new BigInteger(Raw)) * tenPow10;
            BigInteger units = BigInteger.DivRem(scaled, OneRaw, out BigInteger remainder);
            if (remainder * 2 >= OneRaw)
            {
                units += 1;
            }

            BigInteger intPart = BigInteger.DivRem(units, tenPow10, out BigInteger fracPart);
            string fraction = fracPart.ToString(CultureInfo.InvariantCulture).PadLeft(10, '0').TrimEnd('0');
            string sign = Raw < 0 && !units.IsZero ? "-" : string.Empty;
            string integer = intPart.ToString(CultureInfo.InvariantCulture);
            return fraction.Length == 0 ? sign + integer : sign + integer + "." + fraction;
        }

        /// <inheritdoc />
        public bool Equals(Fixed other) => Raw == other.Raw;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Fixed other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Raw.GetHashCode();

        /// <inheritdoc />
        public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

        private static ulong Magnitude(long value) => value < 0 ? unchecked((ulong)(-(value + 1)) + 1UL) : (ulong)value;

        private static long ApplySign(ulong magnitude, bool negative)
        {
            if (negative)
            {
                if (magnitude > SignBit)
                {
                    throw new OverflowException("Fixed arithmetic overflow.");
                }

                return magnitude == SignBit ? long.MinValue : -(long)magnitude;
            }

            if (magnitude > long.MaxValue)
            {
                throw new OverflowException("Fixed arithmetic overflow.");
            }

            return (long)magnitude;
        }

        /// <summary>Computes round((numerator * 2^32) / denominator), halves away from zero.</summary>
        private static long DivideScaled(long numerator, long denominator)
        {
            if (denominator == 0)
            {
                throw new DivideByZeroException("Fixed division by zero.");
            }

            bool negative = (numerator < 0) != (denominator < 0);
            ulong n = Magnitude(numerator);
            ulong d = Magnitude(denominator);
            ulong hi = n >> (64 - FractionBits);
            ulong lo = n << FractionBits;
            if (hi >= d)
            {
                throw new OverflowException("Fixed division overflow.");
            }

            ulong quotient = UInt128Math.Divide(hi, lo, d, out ulong remainder);
            if (remainder >= d - remainder)
            {
                if (quotient == ulong.MaxValue)
                {
                    throw new OverflowException("Fixed division overflow.");
                }

                quotient++;
            }

            return ApplySign(quotient, negative);
        }
    }
}
