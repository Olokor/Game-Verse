using GameVerse.Platform.Matchmaking;

namespace GameVerse.Platform.Tests.Matchmaking;

/// <summary>Glicko-2 against Glickman's published reference values ("Example of the Glicko-2 system", 2013).</summary>
public class Glicko2Tests
{
    [Fact]
    public void Glickmans_worked_example_gives_the_published_values()
    {
        var player = new GlickoRating(1500, 200, 0.06);
        GlickoGame[] games =
        [
            new(new GlickoRating(1400, 30, 0.06), 1),
            new(new GlickoRating(1550, 100, 0.06), 0),
            new(new GlickoRating(1700, 300, 0.06), 0),
        ];

        var after = Glicko2.Update(player, games);

        // The paper: r' = 1464.06, RD' = 151.52, sigma' = 0.05999.
        after.Rating.ShouldBe(1464.06, 0.01);
        after.Deviation.ShouldBe(151.52, 0.01);
        after.Volatility.ShouldBe(0.05999, 0.00001);
    }

    [Fact]
    public void A_period_without_games_only_widens_the_deviation_up_to_350()
    {
        var player = new GlickoRating(1600, 50, 0.06);

        var idle = Glicko2.Update(player, []);

        idle.Rating.ShouldBe(1600);
        idle.Volatility.ShouldBe(0.06);
        idle.Deviation.ShouldBe(Math.Sqrt((50 / Glicko2.Scale * 50 / Glicko2.Scale) + (0.06 * 0.06)) * Glicko2.Scale, 0.0001); // ~50.5
        Glicko2.Idle(player, 10_000).Deviation.ShouldBe(350);
        Glicko2.Idle(player, 0).ShouldBe(player);
    }

    [Fact]
    public void A_win_moves_the_underdog_up_more_than_the_favourite()
    {
        var low = new GlickoRating(1400, 100, 0.06);
        var high = new GlickoRating(1700, 100, 0.06);

        var underdogGain = Glicko2.Update(low, [new GlickoGame(high, 1)]).Rating - low.Rating;
        var favouriteGain = Glicko2.Update(high, [new GlickoGame(low, 1)]).Rating - high.Rating;

        underdogGain.ShouldBeGreaterThan(favouriteGain);
        favouriteGain.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_four_player_finish_is_rated_as_head_to_heads_from_the_ratings_before_the_game()
    {
        GlickoRating[] before = [new(1500, 350, 0.06), new(1520, 200, 0.06), new(1480, 120, 0.06), new(1600, 80, 0.06)];
        int[] places = [2, 1, 4, 3]; // seat 1 won, then seat 0, seat 3, seat 2

        var after = Glicko2.RateFinishingOrder(before, places);

        // Seat 0 came 2nd: beat seats 2 and 3, lost to seat 1.
        after[0].ShouldBe(Glicko2.Update(before[0], [new GlickoGame(before[1], 0), new GlickoGame(before[2], 1), new GlickoGame(before[3], 1)]));
        after[1].Rating.ShouldBeGreaterThan(before[1].Rating, "the winner goes up");
        after[2].Rating.ShouldBeLessThan(before[2].Rating, "last place goes down");
        after.ShouldAllBe(r => r.Deviation < 350);
    }

    [Fact]
    public void Equal_places_count_as_draws()
    {
        GlickoRating[] before = [new(1500, 200, 0.06), new(1500, 200, 0.06)];

        var after = Glicko2.RateFinishingOrder(before, [1, 1]);

        after[0].Rating.ShouldBe(1500, 0.000001);
        after[0].Deviation.ShouldBeLessThan(200);
    }
}

public class RatingBandTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(4.9, 100)]
    [InlineData(5, 150)]
    [InlineData(20, 300)]
    [InlineData(40, 500)]
    [InlineData(59, 500)]
    public void The_band_widens_by_50_every_5_seconds_up_to_500(double seconds, int band) =>
        new MatchmakingOptions().Band(TimeSpan.FromSeconds(seconds)).ShouldBe(band);

    [Fact]
    public void After_a_minute_any_rating_is_accepted() =>
        new MatchmakingOptions().Band(TimeSpan.FromSeconds(60)).ShouldBeNull();
}
