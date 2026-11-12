using GameVerse.Rules.Tests.Fixtures;

namespace GameVerse.Rules.Tests.Ludo;

/// <summary>Replays every fixture in shared/rules-fixtures/ludo: the agreed Ludo rulebook (CLAUDE.md 4.4).</summary>
public class LudoFixtureTests
{
    private const string Game = "ludo";

    public static TheoryData<string> LudoFixtures() => new(FixtureLoader.FileNames(Game));

    [Theory]
    [MemberData(nameof(LudoFixtures))]
    public void Ludo_fixture_passes(string file)
    {
        var adapter = new LudoFixtureAdapter();
        FixtureRunner.Run(adapter, FixtureLoader.Load(Game, file));
        adapter.UnusedDice.ShouldBe(0, $"{file} lists dice it never rolls");
    }
}
