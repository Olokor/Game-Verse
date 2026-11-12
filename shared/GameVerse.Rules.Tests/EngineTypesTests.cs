using GameVerse.Rules.Engine;

namespace GameVerse.Rules.Tests;

public class EngineTypesTests
{
    [Fact]
    public void Ok_result_exposes_state()
    {
        var result = ApplyResult.Ok("s1");
        result.IsOk.ShouldBeTrue();
        result.Error.ShouldBeNull();
        result.State.ShouldBe("s1");
    }

    [Fact]
    public void Rejected_result_has_error_and_no_state()
    {
        var result = ApplyResult.Rejected<string>("not_your_turn", "Seat 1 is to move.");
        result.IsOk.ShouldBeFalse();
        result.Error!.Code.ShouldBe("not_your_turn");
        result.Error.ToString().ShouldBe("not_your_turn: Seat 1 is to move.");
        Should.Throw<InvalidOperationException>(() => result.State).Message.ShouldContain("not_your_turn");
    }

    [Fact]
    public void Outcome_shortcuts_and_validation()
    {
        var win = GameOutcome.Winner(2, "checkmate");
        win.Kind.ShouldBe(OutcomeKind.Win);
        win.WinningSeats.ShouldBe([2]);
        win.Placements.ShouldBeEmpty();

        var draw = GameOutcome.Drawn("stalemate");
        draw.Kind.ShouldBe(OutcomeKind.Draw);
        draw.WinningSeats.ShouldBeEmpty();

        var teams = new GameOutcome(OutcomeKind.Win, [0, 2], [0, 2, 1, 3], "all_tokens_home");
        teams.Placements.ShouldBe([0, 2, 1, 3]);

        Should.Throw<ArgumentException>(() => new GameOutcome(OutcomeKind.Win, null, null, "x"));
        Should.Throw<ArgumentException>(() => new GameOutcome(OutcomeKind.Draw, [1], null, "x"));
        Should.Throw<ArgumentNullException>(() => new GameOutcome(OutcomeKind.Abandoned, null, null, null!));
    }

    [Fact]
    public void Seat_and_error_validation()
    {
        var seat = new SeatInfo(1, "p1", isAI: true);
        seat.Seat.ShouldBe(1);
        seat.PlayerId.ShouldBe("p1");
        seat.IsAI.ShouldBeTrue();

        Should.Throw<ArgumentOutOfRangeException>(() => new SeatInfo(-1, "p", false));
        Should.Throw<ArgumentNullException>(() => new SeatInfo(0, null!, false));
        Should.Throw<ArgumentNullException>(() => new RuleError(null!, "m"));
        Should.Throw<ArgumentNullException>(() => new RuleError("c", null!));
    }
}
