using System.Globalization;
using System.Text.Json;
using GameVerse.Rules.Math;
using GameVerse.Rules.Random;
using GameVerse.Rules.Tests.Fixtures;

namespace GameVerse.Rules.Tests.Random;

public class Xoshiro256StarStarTests
{
    private static JsonDocument Vectors() => FixtureLoader.LoadDocument("prng", "xoshiro256ss.json");

    private static ulong[] ULongs(JsonElement array) => array.EnumerateArray().Select(e => ulong.Parse(e.GetString()!, CultureInfo.InvariantCulture)).ToArray();

    [Fact]
    public void Matches_published_reference_outputs_from_raw_state()
    {
        using var doc = Vectors();
        foreach (var vector in doc.RootElement.GetProperty("fromState").EnumerateArray())
        {
            var rng = Xoshiro256StarStar.FromState(ULongs(vector.GetProperty("state")));
            foreach (var expected in ULongs(vector.GetProperty("outputs")))
            {
                rng.NextULong().ShouldBe(expected);
            }
        }
    }

    [Fact]
    public void Matches_golden_outputs_from_seed()
    {
        using var doc = Vectors();
        foreach (var vector in doc.RootElement.GetProperty("fromSeed").EnumerateArray())
        {
            var rng = new Xoshiro256StarStar(ulong.Parse(vector.GetProperty("seed").GetString()!, CultureInfo.InvariantCulture));
            rng.GetState().ShouldBe(ULongs(vector.GetProperty("state")));
            foreach (var expected in ULongs(vector.GetProperty("outputs")))
            {
                rng.NextULong().ShouldBe(expected);
            }
        }
    }

    [Fact]
    public void Matches_golden_dice_rolls()
    {
        using var doc = Vectors();
        var dice = doc.RootElement.GetProperty("dice");
        var rng = new Xoshiro256StarStar(ulong.Parse(dice.GetProperty("seed").GetString()!, CultureInfo.InvariantCulture));
        var expected = dice.GetProperty("rolls").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        expected.Select(_ => rng.NextInt(6) + 1).ToArray().ShouldBe(expected);
    }

    [Fact]
    public void Same_seed_gives_same_sequence()
    {
        var a = new Xoshiro256StarStar(123456789);
        var b = new Xoshiro256StarStar(123456789);
        for (var i = 0; i < 1000; i++)
        {
            a.NextULong().ShouldBe(b.NextULong());
        }

        new Xoshiro256StarStar(1).NextULong().ShouldNotBe(new Xoshiro256StarStar(2).NextULong());
    }

    [Fact]
    public void Saved_state_resumes_the_exact_sequence()
    {
        var original = new Xoshiro256StarStar(42);
        for (var i = 0; i < 5; i++)
        {
            original.NextULong();
        }

        var restored = Xoshiro256StarStar.FromState(original.GetState());
        for (var i = 0; i < 100; i++)
        {
            restored.NextULong().ShouldBe(original.NextULong());
        }
    }

    [Fact]
    public void GetState_returns_a_copy()
    {
        var rng = new Xoshiro256StarStar(5);
        var state = rng.GetState();
        state[0] = 0;
        rng.GetState()[0].ShouldNotBe(0UL);
    }

    [Fact]
    public void Invalid_state_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => Xoshiro256StarStar.FromState(null!));
        Should.Throw<ArgumentException>(() => Xoshiro256StarStar.FromState(new ulong[3]));
        Should.Throw<ArgumentException>(() => Xoshiro256StarStar.FromState(new ulong[4]));
    }

    [Fact]
    public void NextInt_stays_in_bounds_and_hits_every_value()
    {
        var rng = new Xoshiro256StarStar(9);
        var seen = new int[6];
        for (var i = 0; i < 6000; i++)
        {
            var roll = rng.NextInt(6);
            roll.ShouldBeInRange(0, 5);
            seen[roll]++;
        }

        seen.ShouldAllBe(count => count > 800);
        rng.NextInt(1).ShouldBe(0);
        Should.Throw<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Should.Throw<ArgumentOutOfRangeException>(() => rng.NextInt(-3));
    }

    [Fact]
    public void NextInt_range_stays_in_bounds()
    {
        var rng = new Xoshiro256StarStar(10);
        for (var i = 0; i < 2000; i++)
        {
            rng.NextInt(-3, 4).ShouldBeInRange(-3, 3);
            rng.NextInt(int.MinValue, int.MaxValue).ShouldBeLessThan(int.MaxValue);
        }

        rng.NextInt(7, 8).ShouldBe(7);
        Should.Throw<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }

    [Fact]
    public void NextFixed_is_in_unit_interval()
    {
        var rng = new Xoshiro256StarStar(11);
        for (var i = 0; i < 2000; i++)
        {
            var f = rng.NextFixed();
            (f >= Fixed.Zero).ShouldBeTrue();
            (f < Fixed.One).ShouldBeTrue();
        }
    }

    [Fact]
    public void Shuffle_is_a_deterministic_permutation()
    {
        var a = Enumerable.Range(0, 54).ToList();
        var b = Enumerable.Range(0, 54).ToList();
        new Xoshiro256StarStar(77).Shuffle(a);
        new Xoshiro256StarStar(77).Shuffle(b);

        a.ShouldBe(b);
        a.ShouldNotBe(Enumerable.Range(0, 54).ToList());
        a.OrderBy(x => x).ShouldBe(Enumerable.Range(0, 54));

        var empty = new List<int>();
        new Xoshiro256StarStar(1).Shuffle(empty);
        empty.ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => new Xoshiro256StarStar(1).Shuffle<int>(null!));
    }
}
