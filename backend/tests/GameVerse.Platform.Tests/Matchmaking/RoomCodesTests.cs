using GameVerse.Platform.Matchmaking;

namespace GameVerse.Platform.Tests.Matchmaking;

public class RoomCodesTests
{
    [Fact]
    public void Codes_are_six_characters_without_look_alikes()
    {
        RoomCodes.Alphabet.ShouldNotContain('O');
        RoomCodes.Alphabet.ShouldNotContain('0');
        RoomCodes.Alphabet.ShouldNotContain('I');
        RoomCodes.Alphabet.ShouldNotContain('1');
        RoomCodes.Alphabet.ShouldNotContain('L');
        RoomCodes.Alphabet.Distinct().Count().ShouldBe(RoomCodes.Alphabet.Length);

        var codes = Enumerable.Range(0, 2000).Select(_ => RoomCodes.Generate()).ToList();
        codes.ShouldAllBe(c => c.Length == 6 && c.All(RoomCodes.Alphabet.Contains));
        codes.Distinct().Count().ShouldBeGreaterThan(1990, "31^6 codes: repeats in 2000 draws are rare");
    }

    [Theory]
    [InlineData("abc234", "ABC234")]
    [InlineData("ABC234", "ABC234")]
    [InlineData("ABC23", null)]
    [InlineData("ABC2345", null)]
    [InlineData("ABCO12", null)]
    [InlineData(null, null)]
    public void Typed_codes_are_normalised_or_rejected(string? typed, string? expected) =>
        RoomCodes.Normalize(typed).ShouldBe(expected);
}
