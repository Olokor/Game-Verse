using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using GameVerse.Rules.Random;

namespace GameVerse.Rules.Tests.Ludo;

/// <summary>Engine behaviour the fixtures cannot express: validation, immutability, views, determinism, invariants.</summary>
public class LudoEngineTests
{
    private static readonly LudoEngine Engine = new();

    private static List<SeatInfo> Seats(int n) => Enumerable.Range(0, n).Select(i => new SeatInfo(i, "p" + i, false)).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void NewGame_rejects_seat_counts_outside_2_to_4(int n) =>
        Should.Throw<ArgumentException>(() => Engine.NewGame(LudoConfig.Default, Seats(n), 1));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Teams_need_exactly_four_seats(int n) =>
        Should.Throw<ArgumentException>(() => Engine.NewGame(new LudoConfig(teams: true), Seats(n), 1));

    [Fact]
    public void Placings_are_clamped_to_1_to_3()
    {
        new LudoConfig(placings: 0).Placings.ShouldBe(1);
        new LudoConfig(placings: 9).Placings.ShouldBe(3);
    }

    [Fact]
    public void Default_config_is_classic_naija()
    {
        var d = LudoConfig.Default;
        (d.SixToLeaveBase, d.ThreeSixesForfeit, d.CaptureBonus, d.SafeSquares).ShouldBe((true, true, true, true));
        (d.MustCapture, d.QuickMode, d.Teams, d.Placings).ShouldBe((false, false, false, 1));
    }

    [Fact]
    public void Apply_never_changes_the_input_state()
    {
        var state = Engine.NewGame(LudoConfig.Default, Seats(4), 7);
        var rng = new Xoshiro256StarStar(99);
        for (var i = 0; i < 300 && state.Phase != LudoPhase.Over; i++)
        {
            var before = Snapshot(state);
            var legal = Engine.LegalActions(state, state.CurrentSeat);
            var next = Engine.Apply(state, state.CurrentSeat, legal[rng.NextInt(0, legal.Count)]);
            Snapshot(state).ShouldBe(before);
            state = next.State;
        }
    }

    [Fact]
    public void ViewFor_hides_the_dice_but_keeps_the_board()
    {
        var state = Engine.NewGame(new LudoConfig(quickMode: true), Seats(2), 5);
        var view = Engine.ViewFor(state, 1);

        state.RngState.ShouldNotBeNull();
        view.RngState.ShouldBeNull();
        view.Positions.ShouldBe(state.Positions);
        view.CurrentSeat.ShouldBe(state.CurrentSeat);
        view.MoveNumber.ShouldBe(state.MoveNumber);
        Engine.LegalActions(view, 0).ShouldBe([LudoAction.Roll]);

        var roll = Engine.Apply(view, 0, LudoAction.Roll);
        roll.IsOk.ShouldBeFalse();
        roll.Error!.Code.ShouldBe("dice_hidden");
    }

    [Fact]
    public void The_same_seed_replays_the_same_game_and_another_seed_does_not()
    {
        Rolls(PlayRandom(4, LudoConfig.Default, seed: 1234, choiceSeed: 1)).ShouldBe(Rolls(PlayRandom(4, LudoConfig.Default, seed: 1234, choiceSeed: 1)));
        Rolls(PlayRandom(4, LudoConfig.Default, seed: 1234, choiceSeed: 1)).ShouldNotBe(Rolls(PlayRandom(4, LudoConfig.Default, seed: 1235, choiceSeed: 1)));
    }

    [Fact]
    public void A_revealed_secret_reproduces_the_server_dice()
    {
        // Commit-reveal: the server publishes Commit(secret) at creation and seeds the engine with DeriveSeed(secret).
        var secret = Enumerable.Range(0, SeedCommitment.SecretLength).Select(i => (byte)(i * 7)).ToArray();
        var commitment = SeedCommitment.Commit(secret);
        var server = PlayRandom(2, LudoConfig.Default, SeedCommitment.DeriveSeed(secret), choiceSeed: 3);

        // After the match a player receives the secret, checks it against the commitment and replays the dice.
        SeedCommitment.Verify(commitment, secret).ShouldBeTrue();
        Rolls(PlayRandom(2, LudoConfig.Default, SeedCommitment.DeriveSeed(secret), choiceSeed: 3)).ShouldBe(Rolls(server));
    }

    [Fact]
    public void Dice_are_fair_enough_over_many_rolls()
    {
        var state = Engine.NewGame(LudoConfig.Default, Seats(2), 2024);
        var counts = new int[7];
        var rng = Xoshiro256StarStar.FromState(state.RngState!);
        for (var i = 0; i < 60_000; i++)
        {
            counts[rng.NextInt(1, 7)]++;
        }

        counts.Skip(1).ShouldAllBe(c => c > 9_500 && c < 10_500);
    }

    public static TheoryData<int> GameBatches() => new(Enumerable.Range(0, 8));

    /// <summary>200 seeded random games (25 per batch) across player counts and every house-rule combination.</summary>
    [Theory]
    [MemberData(nameof(GameBatches))]
    public void Random_games_keep_the_invariants_and_always_end(int batch)
    {
        for (var g = batch * 25; g < (batch + 1) * 25; g++)
        {
            var teams = g % 5 == 4;
            var players = teams ? 4 : 2 + (g % 3);
            var config = new LudoConfig(
                sixToLeaveBase: (g & 1) == 0,
                threeSixesForfeit: (g & 2) == 0,
                captureBonus: (g & 4) == 0,
                mustCapture: (g & 8) != 0,
                safeSquares: (g & 16) == 0,
                quickMode: (g & 32) != 0,
                placings: 1 + (g % 3),
                teams: teams);
            var history = PlayRandom(players, config, seed: (ulong)(1000 + g), choiceSeed: (ulong)g, checkStep: CheckStep);
            var last = history[^1];
            last.Phase.ShouldBe(LudoPhase.Over, $"game {g} did not end");

            var outcome = Engine.Outcome(last)!;
            outcome.Placements.OrderBy(s => s).ShouldBe(Enumerable.Range(0, players), $"game {g}");
            outcome.WinningSeats.ShouldContain(outcome.Placements[0]);
            outcome.WinningSeats.ShouldAllBe(s => last.AllHome(s));
            if (!teams)
            {
                last.Finished.Count.ShouldBe(System.Math.Min(config.Placings, players - 1), $"game {g}");
            }
        }
    }

    private static void CheckStep(LudoState before, LudoAction action, LudoState after)
    {
        after.Positions.Count.ShouldBe(before.Positions.Count);
        after.Positions.ShouldAllBe(p => p >= LudoBoard.Base && p <= LudoBoard.Home);
        after.MoveNumber.ShouldBe(before.MoveNumber + 1);
        after.Finished.Take(before.Finished.Count).ShouldBe(before.Finished);
        after.Finished.ShouldAllBe(s => after.AllHome(s));
        if (after.Phase == LudoPhase.Move)
        {
            after.LastRoll.ShouldBeInRange(1, 6);
        }

        var mover = LudoEngine.ControlledSeat(before, before.CurrentSeat);
        for (var i = 0; i < after.Positions.Count; i++)
        {
            if (after.Positions[i] >= before.Positions[i])
            {
                continue;
            }

            // A token only ever goes backwards by being captured: to base, from the shared track, by an opponent's move.
            var owner = i / LudoBoard.TokensPerSeat;
            action.IsRoll.ShouldBeFalse();
            after.Positions[i].ShouldBe(LudoBoard.Base);
            before.Positions[i].ShouldBeInRange(0, LudoBoard.LastTrackPosition);
            owner.ShouldNotBe(mover);
            if (before.Config.Teams)
            {
                owner.ShouldNotBe(LudoEngine.Partner(mover));
            }
        }
    }

    private static List<LudoState> PlayRandom(int players, LudoConfig config, ulong seed, ulong choiceSeed, Action<LudoState, LudoAction, LudoState>? checkStep = null)
    {
        var state = Engine.NewGame(config, Seats(players), seed);
        var choices = new Xoshiro256StarStar(choiceSeed);
        var history = new List<LudoState> { state };
        for (var i = 0; i < 50_000 && state.Phase != LudoPhase.Over; i++)
        {
            var legal = Engine.LegalActions(state, state.CurrentSeat);
            legal.ShouldNotBeEmpty("the seat to act always has an action: no stalls");
            for (var other = 0; other < players; other++)
            {
                if (other != state.CurrentSeat)
                {
                    Engine.LegalActions(state, other).ShouldBeEmpty();
                }
            }

            var action = legal[choices.NextInt(0, legal.Count)];
            var result = Engine.Apply(state, state.CurrentSeat, action);
            result.IsOk.ShouldBeTrue(result.Error?.Code);
            checkStep?.Invoke(state, action, result.State);
            state = result.State;
            history.Add(state);
        }

        return history;
    }

    private static List<int> Rolls(List<LudoState> history) =>
        history.Zip(history.Skip(1)).Where(p => p.First.Phase == LudoPhase.Roll).Select(p => p.Second.LastRoll).ToList(); // the roll phase only allows a roll

    private static string Snapshot(LudoState s) =>
        string.Join(",", s.Positions) + "|" + s.CurrentSeat + "|" + s.Phase + "|" + s.LastRoll + "|" + s.SixesInRow + "|"
        + string.Join(",", s.Finished) + "|" + string.Join(",", s.RngState ?? []) + "|" + s.MoveNumber;
}
