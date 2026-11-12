using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using GameVerse.Rules.Random;

namespace GameVerse.Rules.Tests.Ludo;

public class LudoAiTests
{
    private const int B = LudoBoard.Base;
    private const int H = LudoBoard.Home;
    private static readonly LudoEngine Engine = new();

    private static List<SeatInfo> Seats(int n) => Enumerable.Range(0, n).Select(i => new SeatInfo(i, "p" + i, true)).ToList();

    /// <summary>A 2-player state where seat 0 must move with <paramref name="die"/>.</summary>
    private static LudoState MoveState(int die, int[] seat0, int[] seat1, LudoConfig? config = null) =>
        new(config ?? LudoConfig.Default, LudoBoard.ColorsFor(2), seat0.Concat(seat1).ToArray(), 0, LudoPhase.Move, die, die == 6 ? 1 : 0, [], null, 0);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Prefers_a_capture(int level)
    {
        // Token 0 (33 -> 36) captures seat 1's token on absolute 36; token 1 (10 -> 13) just moves.
        var state = MoveState(3, [33, 10, B, B], [10, B, B, B]);
        LudoAi.Choose(state, 0, level, new Xoshiro256StarStar(1)).ShouldBe(LudoAction.Move(0));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Prefers_reaching_home(int level)
    {
        var state = MoveState(3, [53, 20, B, B], [B, B, B, B]);
        LudoAi.Choose(state, 0, level, new Xoshiro256StarStar(1)).ShouldBe(LudoAction.Move(0));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Runs_from_a_threatened_square(int level)
    {
        // Seat 1's token at relative 15 is on absolute 41, three behind seat 0's token 0 on 44. Token 1 is safe at 0.
        // With a 3, token 0 escapes onto the star at 47; moving token 1 instead leaves token 0 exposed to a 3.
        var state = MoveState(3, [44, 0, B, B], [15, B, B, B]);
        LudoAi.Choose(state, 0, level, new Xoshiro256StarStar(1)).ShouldBe(LudoAction.Move(0));
    }

    [Fact]
    public void Level_3_values_finishing_second_when_placings_is_2()
    {
        // Rules-auditor case: seat 1 already finished. Bringing token 2 home (53 -> 56) earns a bonus roll that can finish
        // seat 0 in second place, which ends the game in a good place for it, not a loss.
        var positions = new[] { H, H, 53, 50, H, H, H, H, 10, B, B, B };
        var state = new LudoState(new LudoConfig(placings: 2), LudoBoard.ColorsFor(3), positions, 0, LudoPhase.Move, 3, 0, [1], null, 0);
        LudoAi.Choose(state, 0, 3, new Xoshiro256StarStar(1)).ShouldBe(LudoAction.Move(2));
    }

    [Fact]
    public void Rolls_in_the_roll_phase_and_refuses_out_of_turn()
    {
        var state = Engine.NewGame(LudoConfig.Default, Seats(2), 1);
        LudoAi.Choose(state, 0, 3, new Xoshiro256StarStar(1)).ShouldBe(LudoAction.Roll);
        Should.Throw<InvalidOperationException>(() => LudoAi.Choose(state, 1, 3, new Xoshiro256StarStar(1)));
    }

    [Fact]
    public void Works_on_a_player_view_without_the_dice()
    {
        var view = Engine.ViewFor(MoveState(6, [B, 20, B, B], [B, B, B, B]), 0);
        var choice = LudoAi.Choose(view, 0, 3, new Xoshiro256StarStar(1));
        Engine.LegalActions(view, 0).ShouldContain(choice);
    }

    [Fact]
    public void Is_deterministic_for_the_same_inputs()
    {
        var state = MoveState(6, [B, 20, 30, B], [25, B, B, B]);
        for (var level = 1; level <= 3; level++)
        {
            LudoAi.Choose(state, 0, level, new Xoshiro256StarStar(5)).ShouldBe(LudoAi.Choose(state, 0, level, new Xoshiro256StarStar(5)));
        }
    }

    public static TheoryData<int, int> LevelsAndPlayers()
    {
        var data = new TheoryData<int, int>();
        foreach (var level in new[] { 1, 2, 3 })
        {
            foreach (var players in new[] { 2, 3, 4 })
            {
                data.Add(level, players);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LevelsAndPlayers))]
    public void Only_ever_chooses_legal_actions_through_whole_games(int level, int players)
    {
        for (var g = 0; g < 10; g++)
        {
            var config = new LudoConfig(mustCapture: g % 2 == 1, teams: players == 4 && g % 3 == 0, placings: 1 + (g % 3));
            var state = Engine.NewGame(config, Seats(players), (ulong)(500 + g));
            var rng = new Xoshiro256StarStar((ulong)g);
            for (var i = 0; i < 20_000 && state.Phase != LudoPhase.Over; i++)
            {
                var choice = LudoAi.Choose(state, state.CurrentSeat, level, rng);
                Engine.LegalActions(state, state.CurrentSeat).ShouldContain(choice);
                state = Engine.Apply(state, state.CurrentSeat, choice).State;
            }

            state.Phase.ShouldBe(LudoPhase.Over);
        }
    }

    [Theory]
    [InlineData(3, 1, 85)] // measured 95/100
    [InlineData(2, 1, 80)] // measured 89/100
    [InlineData(3, 2, 55)] // measured 59/100
    public void A_stronger_level_beats_a_weaker_one(int strong, int weak, int minWinsOf100)
    {
        var wins = 0;
        for (var g = 0; g < 100; g++)
        {
            // Alternate who starts so seat 0's first-move advantage cancels out.
            var strongSeat = g % 2;
            var state = Engine.NewGame(LudoConfig.Default, Seats(2), (ulong)(9000 + g));
            var rng = new Xoshiro256StarStar((ulong)(77 + g));
            while (state.Phase != LudoPhase.Over)
            {
                var level = state.CurrentSeat == strongSeat ? strong : weak;
                state = Engine.Apply(state, state.CurrentSeat, LudoAi.Choose(state, state.CurrentSeat, level, rng)).State;
            }

            if (Engine.Outcome(state)!.WinningSeats.Contains(strongSeat))
            {
                wins++;
            }
        }

        wins.ShouldBeGreaterThanOrEqualTo(minWinsOf100, $"level {strong} won {wins}/100 against level {weak}");
    }
}
