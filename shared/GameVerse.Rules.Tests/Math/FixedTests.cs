using System.Numerics;
using GameVerse.Rules.Math;
using GameVerse.Rules.Random;

namespace GameVerse.Rules.Tests.Math;

public class FixedTests
{
    private const long One = 1L << 32;

    private static Fixed F(string s) => Fixed.Parse(s);

    [Fact]
    public void Exact_known_values()
    {
        (F("1.5") * F("2.25")).ShouldBe(F("3.375"));
        (F("1.5") * F("2.25")).Raw.ShouldBe(27L * One / 8);
        (F("7") / F("2")).ShouldBe(F("3.5"));
        (F("0.1") + F("0.2") - F("0.3")).Raw.ShouldBe(0);
        (Fixed.FromInt(46340) * Fixed.FromInt(46340)).ShouldBe(Fixed.FromInt(2147395600));
        F("-12.375").Raw.ShouldBe(-12L * One - (3L * One / 8));
    }

    [Fact]
    public void Ratios_round_to_nearest_with_halves_away_from_zero()
    {
        Fixed.FromRatio(1, 3).Raw.ShouldBe(1431655765);
        Fixed.FromRatio(2, 3).Raw.ShouldBe(2863311531);
        Fixed.FromRatio(-2, 3).Raw.ShouldBe(-2863311531);
        Fixed.FromRatio(2, -3).Raw.ShouldBe(-2863311531);
        (Fixed.One / Fixed.FromInt(3)).ShouldBe(Fixed.FromRatio(1, 3));

        // Exactly half an ulp: 1 raw * 0.5 = 0.5 raw rounds away from zero.
        (Fixed.Epsilon * Fixed.Half).Raw.ShouldBe(1);
        (-Fixed.Epsilon * Fixed.Half).Raw.ShouldBe(-1);
        (Fixed.Epsilon * Fixed.Epsilon).Raw.ShouldBe(0);
        (Fixed.Epsilon / Fixed.FromInt(2)).Raw.ShouldBe(1);
        (Fixed.Epsilon / Fixed.FromInt(3)).Raw.ShouldBe(0);
    }

    [Fact]
    public void Square_root_is_exact_and_rounded_to_nearest()
    {
        Fixed.Sqrt(Fixed.FromInt(4)).ShouldBe(Fixed.FromInt(2));
        Fixed.Sqrt(Fixed.FromInt(144)).ShouldBe(Fixed.FromInt(12));
        Fixed.Sqrt(F("2.25")).ShouldBe(F("1.5"));
        Fixed.Sqrt(Fixed.Zero).ShouldBe(Fixed.Zero);
        Fixed.Sqrt(Fixed.FromInt(2)).Raw.ShouldBe(6074001000);
        Fixed.Sqrt(Fixed.Half).Raw.ShouldBe(3037000500);
        Fixed.Sqrt(Fixed.FromInt(10)).Raw.ShouldBe(13581879131);
        Should.Throw<ArgumentOutOfRangeException>(() => Fixed.Sqrt(-Fixed.One));
    }

    [Fact]
    public void Square_root_matches_big_integer_reference()
    {
        var rng = new Xoshiro256StarStar(7);
        for (var i = 0; i < 2000; i++)
        {
            var raw = (long)(rng.NextULong() >> (1 + rng.NextInt(63)));
            var target = new BigInteger(raw) << 32;
            var root = IntegerSqrt(target);
            if (target > root * root + root)
            {
                root += 1;
            }

            Fixed.Sqrt(Fixed.FromRaw(raw)).Raw.ShouldBe((long)root, "raw=" + raw);
        }

        Fixed.Sqrt(Fixed.MaxValue).Raw.ShouldBe((long)RoundedSqrt(new BigInteger(long.MaxValue) << 32));
    }

    [Fact]
    public void Multiply_and_divide_match_big_integer_reference()
    {
        var rng = new Xoshiro256StarStar(2026);
        for (var i = 0; i < 20000; i++)
        {
            var a = RandomRaw(rng);
            var b = RandomRaw(rng);

            CheckAgainstReference(a, b, (x, y) => x * y, (x, y) => RoundHalfAway(x * y, One), "mul");
            if (b != 0)
            {
                CheckAgainstReference(a, b, (x, y) => x / y, (x, y) => RoundHalfAway(x * One, y), "div");
            }
        }
    }

    [Fact]
    public void Overflow_throws_instead_of_wrapping()
    {
        Should.Throw<OverflowException>(() => Fixed.MaxValue + Fixed.Epsilon);
        Should.Throw<OverflowException>(() => Fixed.MinValue - Fixed.Epsilon);
        Should.Throw<OverflowException>(() => -Fixed.MinValue);
        Should.Throw<OverflowException>(() => Fixed.Abs(Fixed.MinValue));
        Should.Throw<OverflowException>(() => Fixed.FromInt(65536) * Fixed.FromInt(65536));
        Should.Throw<OverflowException>(() => Fixed.FromInt(-65536) * Fixed.FromInt(65536));
        Should.Throw<OverflowException>(() => Fixed.MinValue * -Fixed.One);
        Should.Throw<OverflowException>(() => Fixed.MaxValue / Fixed.Half);
        Should.Throw<OverflowException>(() => Fixed.MinValue / -Fixed.One);
        Should.Throw<OverflowException>(() => Fixed.MaxValue / Fixed.Epsilon);
        Should.Throw<OverflowException>(() => Fixed.Ceiling(Fixed.MaxValue));
        Should.Throw<DivideByZeroException>(() => Fixed.One / Fixed.Zero);
        Should.Throw<DivideByZeroException>(() => Fixed.FromRatio(1, 0));
    }

    [Fact]
    public void Extremes_that_fit_do_not_throw()
    {
        (Fixed.MinValue * Fixed.One).ShouldBe(Fixed.MinValue);
        (Fixed.MinValue / Fixed.One).ShouldBe(Fixed.MinValue);
        (Fixed.MaxValue * Fixed.One).ShouldBe(Fixed.MaxValue);
        (-Fixed.MaxValue).Raw.ShouldBe(-long.MaxValue);
        Fixed.FromInt(int.MinValue).ShouldBe(Fixed.MinValue);
    }

    [Fact]
    public void Negative_numbers_floor_ceiling_and_round()
    {
        (Fixed.FromInt(-3) * F("2.5")).ShouldBe(F("-7.5"));
        (Fixed.FromInt(-7) / Fixed.FromInt(2)).ShouldBe(F("-3.5"));
        Fixed.Floor(F("-1.5")).ShouldBe(Fixed.FromInt(-2));
        Fixed.Floor(F("1.5")).ShouldBe(Fixed.One);
        Fixed.Ceiling(F("-1.5")).ShouldBe(-Fixed.One);
        Fixed.Ceiling(F("1.25")).ShouldBe(Fixed.FromInt(2));
        Fixed.Ceiling(Fixed.FromInt(3)).ShouldBe(Fixed.FromInt(3));
        Fixed.Round(F("2.5")).ShouldBe(Fixed.FromInt(3));
        Fixed.Round(F("-2.5")).ShouldBe(Fixed.FromInt(-3));
        Fixed.Round(F("2.4999")).ShouldBe(Fixed.FromInt(2));
        F("-1.5").FloorToInt().ShouldBe(-2);
        F("-1.5").RoundToInt().ShouldBe(-2);
        F("1.5").RoundToInt().ShouldBe(2);
    }

    [Fact]
    public void Comparison_helpers()
    {
        var a = F("1.5");
        var b = F("-2");
        Fixed.Min(a, b).ShouldBe(b);
        Fixed.Max(a, b).ShouldBe(a);
        Fixed.Abs(b).ShouldBe(Fixed.FromInt(2));
        Fixed.Abs(a).ShouldBe(a);
        Fixed.Clamp(F("5"), b, a).ShouldBe(a);
        Fixed.Clamp(F("-5"), b, a).ShouldBe(b);
        Fixed.Clamp(Fixed.Zero, b, a).ShouldBe(Fixed.Zero);
        Should.Throw<ArgumentException>(() => Fixed.Clamp(Fixed.Zero, a, b));

        (a > b).ShouldBeTrue();
        (a >= F("1.5")).ShouldBeTrue();
        (b < a).ShouldBeTrue();
        (b <= F("-2")).ShouldBeTrue();
        (a != b).ShouldBeTrue();
        a.CompareTo(b).ShouldBe(1);
        a.Equals((object)F("1.5")).ShouldBeTrue();
        a.Equals("1.5").ShouldBeFalse();
        a.GetHashCode().ShouldBe(F("1.5").GetHashCode());
    }

    [Fact]
    public void Named_operator_alternatives_match_operators()
    {
        var a = F("3.25");
        var b = F("-1.5");
        Fixed.Add(a, b).ShouldBe(a + b);
        Fixed.Subtract(a, b).ShouldBe(a - b);
        Fixed.Multiply(a, b).ShouldBe(a * b);
        Fixed.Divide(a, b).ShouldBe(a / b);
        Fixed.Negate(a).ShouldBe(-a);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("-12.375", "-12.375")]
    [InlineData("+2.50", "2.5")]
    [InlineData("  .5 ", "0.5")]
    [InlineData("7.", "7")]
    [InlineData("-0", "0")]
    // The smallest step is 2^-32 (about 0.00000000023), so tiny inputs round to the nearest step.
    [InlineData("0.0000000001", "0")]
    [InlineData("0.0000000003", "0.0000000002")]
    [InlineData("-0.0000000003", "-0.0000000002")]
    [InlineData("-0.00000000001", "0")]
    public void Parse_and_format(string input, string expected)
    {
        Fixed.Parse(input).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("1e5")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    public void Invalid_or_out_of_range_text_is_rejected(string? input)
    {
        Fixed.TryParse(input, out var value).ShouldBeFalse();
        value.ShouldBe(Fixed.Zero);
    }

    [Fact]
    public void Parse_throws_on_bad_text()
    {
        Should.Throw<FormatException>(() => Fixed.Parse("nope"));
    }

    [Fact]
    public void ToString_round_trips_exactly_through_Parse()
    {
        var rng = new Xoshiro256StarStar(99);
        for (var i = 0; i < 5000; i++)
        {
            var value = Fixed.FromRaw(RandomRaw(rng));
            Fixed.Parse(value.ToString()).ShouldBe(value, "raw=" + value.Raw);
        }

        Fixed.Parse(Fixed.MaxValue.ToString()).ShouldBe(Fixed.MaxValue);
        Fixed.Parse(Fixed.MinValue.ToString()).ShouldBe(Fixed.MinValue);
        Fixed.Parse(Fixed.Epsilon.ToString()).ShouldBe(Fixed.Epsilon);
    }

    [Fact]
    public void Constants_and_display_conversion()
    {
        Fixed.One.Raw.ShouldBe(One);
        Fixed.Half.Raw.ShouldBe(One / 2);
        Fixed.Epsilon.Raw.ShouldBe(1);
        Fixed.FromInt(-5).ToDouble().ShouldBe(-5.0);
        F("0.25").ToDouble().ShouldBe(0.25);
        Fixed.FractionBits.ShouldBe(32);
    }

    /// <summary>Raw values spread over every magnitude, including small and huge.</summary>
    private static long RandomRaw(Xoshiro256StarStar rng) => (long)rng.NextULong() >> rng.NextInt(64);

    private static void CheckAgainstReference(long a, long b, Func<Fixed, Fixed, Fixed> op, Func<BigInteger, BigInteger, BigInteger> reference, string name)
    {
        var expected = reference(a, b);
        if (expected > long.MaxValue || expected < long.MinValue)
        {
            Should.Throw<OverflowException>(() => op(Fixed.FromRaw(a), Fixed.FromRaw(b)), name + " a=" + a + " b=" + b);
        }
        else
        {
            op(Fixed.FromRaw(a), Fixed.FromRaw(b)).Raw.ShouldBe((long)expected, name + " a=" + a + " b=" + b);
        }
    }

    /// <summary>round(n / d), exact halves away from zero.</summary>
    private static BigInteger RoundHalfAway(BigInteger n, BigInteger d)
    {
        var negative = (n.Sign < 0) != (d.Sign < 0);
        var q = BigInteger.DivRem(BigInteger.Abs(n), BigInteger.Abs(d), out var r);
        if (r * 2 >= BigInteger.Abs(d))
        {
            q += 1;
        }

        return negative ? -q : q;
    }

    private static BigInteger IntegerSqrt(BigInteger n)
    {
        if (n.IsZero)
        {
            return 0;
        }

        var x = (BigInteger)System.Math.Sqrt((double)n);
        while (x * x > n)
        {
            x -= 1;
        }

        while ((x + 1) * (x + 1) <= n)
        {
            x += 1;
        }

        return x;
    }

    private static BigInteger RoundedSqrt(BigInteger n)
    {
        var root = IntegerSqrt(n);
        return n > root * root + root ? root + 1 : root;
    }
}
