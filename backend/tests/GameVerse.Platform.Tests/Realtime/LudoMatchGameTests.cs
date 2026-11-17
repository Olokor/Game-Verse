using System.Text.Json;
using GameVerse.Platform.Sessions;
using GameVerse.Rules.Ludo;
using GameVerse.Rules.Random;

namespace GameVerse.Platform.Tests.Realtime;

/// <summary>The Ludo adapter the match host drives: wire actions, timeouts and snapshots.</summary>
public class LudoMatchGameTests
{
    private static readonly IMatchGame Ludo = new MatchGames().Find("ludo")!;

    [Theory]
    [InlineData("{\"roll\":true}", true)]
    [InlineData("{\"forfeit\":true}", true)]
    [InlineData("{\"roll\":false}", false)]
    [InlineData("{\"move\":4}", false)]
    [InlineData("{\"move\":-1}", false)]
    [InlineData("{\"move\":\"1\"}", false)]
    [InlineData("{\"roll\":true,\"move\":0}", false)]
    [InlineData("[]", false)]
    [InlineData("{}", false)]
    public void Only_well_formed_actions_reach_the_engine(string json, bool wellFormed)
    {
        var state = Ludo.NewGame(new Dictionary<string, string>(), 2, 7);

        var result = Ludo.Apply(state, 0, JsonDocument.Parse(json).RootElement);

        (result.ErrorCode != "bad_action").ShouldBe(wellFormed);
    }

    [Fact]
    public void A_snapshot_restores_the_exact_state_including_the_dice()
    {
        var rules = new Dictionary<string, string> { ["quickMode"] = "true", ["placings"] = "2" };
        var state = Ludo.NewGame(rules, 3, 42);
        var rng = new Xoshiro256StarStar(1);
        for (var i = 0; i < 25 && Ludo.ActingSeat(state) is { } seat; i++)
        {
            state = Ludo.Apply(state, seat, Ludo.AiAction(state, seat, 1, rng)).State!;
        }

        var restored = Ludo.Deserialize(Ludo.Serialize(state));

        Ludo.Serialize(restored).ShouldBe(Ludo.Serialize(state));
        var roll = JsonDocument.Parse("{\"roll\":true}").RootElement;
        var seatNow = Ludo.ActingSeat(state)!.Value;
        if (((LudoState)state).Phase == LudoPhase.Roll)
        {
            Ludo.View(Ludo.Apply(restored, seatNow, roll).State!, 0).ToString()
                .ShouldBe(Ludo.View(Ludo.Apply(state, seatNow, roll).State!, 0).ToString(), "the same dice come next");
        }
    }

    [Fact]
    public void The_timeout_action_is_always_legal_and_a_view_never_holds_the_dice()
    {
        var state = Ludo.NewGame(new Dictionary<string, string>(), 4, 99);
        for (var i = 0; i < 400 && Ludo.ActingSeat(state) is { } seat; i++)
        {
            var applied = Ludo.Apply(state, seat, Ludo.TimeoutAction(state, seat));
            applied.IsOk.ShouldBeTrue(applied.ErrorCode);
            state = applied.State!;
            Ludo.View(state, seat).ToString().ShouldNotContain("rng", Case.Insensitive);
        }
    }

    [Fact]
    public void A_forfeit_out_of_turn_is_accepted()
    {
        var state = Ludo.NewGame(new Dictionary<string, string>(), 2, 3);

        var result = Ludo.Apply(state, 1, Ludo.ForfeitAction);

        result.IsOk.ShouldBeTrue();
        Ludo.Outcome(result.State!).ShouldNotBeNull().Reason.ShouldBe("opponents_left");
    }
}
