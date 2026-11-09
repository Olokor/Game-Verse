using GameVerse.Rules.Tests.SelfTest;

namespace GameVerse.Rules.Tests.Fixtures;

public class FixtureRunnerTests
{
    private const string Game = "_selftest";

    public static TheoryData<string> SelfTestFixtures() => new(FixtureLoader.FileNames(Game));

    [Theory]
    [MemberData(nameof(SelfTestFixtures))]
    public void Self_test_fixture_passes(string file)
    {
        FixtureRunner.Run(new NimFixtureAdapter(), FixtureLoader.Load(Game, file));
    }

    [Fact]
    public void Self_test_fixtures_exist()
    {
        FixtureLoader.FileNames(Game).Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Wrong_state_expectation_fails_with_step_and_path()
    {
        var fixture = FixtureLoader.Parse(
            """
            { "game": "_selftest", "name": "bad", "config": { "pile": 5, "maxTake": 3 },
              "seats": [ { "seat": 0, "playerId": "a" }, { "seat": 1, "playerId": "b" } ],
              "steps": [ { "seat": 0, "action": { "take": 2 }, "expect": { "state": { "pile": 4 } } } ] }
            """,
            "inline.json");

        var ex = Should.Throw<FixtureMismatchException>(() => FixtureRunner.Run(new NimFixtureAdapter(), fixture));
        ex.Message.ShouldContain("step 1 (seat 0)");
        ex.Message.ShouldContain("state.pile: expected 4, got 3");
    }

    [Theory]
    [InlineData("""{ "seat": 1, "action": { "take": 1 }, "expect": {} }""", "expected the action to be accepted")]
    [InlineData("""{ "seat": 0, "action": { "take": 1 }, "expect": { "ok": false, "error": "illegal_take" } }""", "but the action was accepted")]
    [InlineData("""{ "seat": 0, "action": { "take": 9 }, "expect": { "ok": false, "error": "not_your_turn" } }""", "expected error 'not_your_turn', got 'illegal_take'")]
    [InlineData("""{ "seat": 0, "action": { "take": 1 }, "expect": { "legalActions": [ { "take": 1 } ] } }""", "legal actions differ")]
    [InlineData("""{ "seat": 0, "action": { "take": 1 }, "expect": { "state": { "nope": 1 } } }""", "state.nope: missing")]
    [InlineData("""{ "seat": 0, "action": { "take": 1 }, "expect": { "state": { "pile": { "x": 1 } } } }""", "expected an object")]
    public void Step_mismatches_are_reported(string step, string expectedMessage)
    {
        var fixture = FixtureLoader.Parse(
            """{ "game": "_selftest", "name": "bad", "config": { "pile": 5, "maxTake": 3 }, "seats": [ { "seat": 0, "playerId": "a" }, { "seat": 1, "playerId": "b" } ], "steps": [ """ + step + " ] }",
            "inline.json");

        Should.Throw<FixtureMismatchException>(() => FixtureRunner.Run(new NimFixtureAdapter(), fixture))
            .Message.ShouldContain(expectedMessage);
    }

    [Theory]
    [InlineData("""{ "kind": "none" }""", 1, "expected the game to still be running")]
    [InlineData("""{ "kind": "win" }""", 0, "but the game is still running")]
    [InlineData("""{ "kind": "draw" }""", 1, "expected kind 'draw', got 'win'")]
    [InlineData("""{ "kind": "win", "winningSeats": [1] }""", 1, "expected winning seats [1], got [0]")]
    [InlineData("""{ "kind": "win", "reason": "other" }""", 1, "expected reason 'other', got 'took_last'")]
    public void Outcome_mismatches_are_reported(string outcome, int pile, string expectedMessage)
    {
        var steps = pile == 1 ? """[ { "seat": 0, "action": { "take": 1 } } ]""" : "[]";
        var fixture = FixtureLoader.Parse(
            """{ "game": "_selftest", "name": "bad", "config": { "pile": """ + pile + """, "maxTake": 3 }, "seats": [ { "seat": 0, "playerId": "a" }, { "seat": 1, "playerId": "b" } ], "steps": """ + steps + """, "outcome": """ + outcome + " }",
            "inline.json");

        Should.Throw<FixtureMismatchException>(() => FixtureRunner.Run(new NimFixtureAdapter(), fixture))
            .Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public void Fixture_in_wrong_folder_is_rejected()
    {
        const string tempGame = "_tmp-wrong-folder-test";
        var dir = FixtureLoader.FixtureDir(tempGame);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "x.json"), """{ "game": "ludo", "name": "x" }""");
            Should.Throw<InvalidDataException>(() => FixtureLoader.Load(tempGame, "x.json")).Message.ShouldContain("'ludo'");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_game_folder_is_reported()
    {
        Should.Throw<DirectoryNotFoundException>(() => FixtureLoader.FileNames("no-such-game"));
    }
}
