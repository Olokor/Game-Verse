using System.Text.Json;
using System.Text.Json.Nodes;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Tests.Fixtures;

namespace GameVerse.Rules.Tests.SelfTest;

// A deliberately tiny two-player game that exists only to prove the IGameEngine contract and the
// fixture runner work end to end. It lives in the test project so no fake game ships in GameVerse.Rules.
// Rules: one pile; players alternate taking 1..MaxTake; whoever takes the last item wins.

public sealed record NimConfig(int Pile, int MaxTake);

public sealed record NimState(int Pile, int MaxTake, int ToMove, int? Winner);

public sealed record NimAction(int Take);

public sealed class NimEngine : IGameEngine<NimState, NimAction, NimConfig>
{
    public NimState NewGame(NimConfig config, IReadOnlyList<SeatInfo> seats, ulong seed)
    {
        if (seats.Count != 2)
        {
            throw new ArgumentException("Nim needs exactly 2 seats.", nameof(seats));
        }

        return new NimState(config.Pile, config.MaxTake, ToMove: 0, Winner: null);
    }

    public IReadOnlyList<NimAction> LegalActions(NimState state, int seat)
    {
        if (state.Winner != null || seat != state.ToMove)
        {
            return [];
        }

        return Enumerable.Range(1, System.Math.Min(state.MaxTake, state.Pile)).Select(n => new NimAction(n)).ToList();
    }

    public ApplyResult<NimState> Apply(NimState state, int seat, NimAction action)
    {
        if (state.Winner != null)
        {
            return ApplyResult.Rejected<NimState>("game_over", "The game has ended.");
        }

        if (seat != state.ToMove)
        {
            return ApplyResult.Rejected<NimState>("not_your_turn", "Seat " + state.ToMove + " is to move.");
        }

        if (action.Take < 1 || action.Take > state.MaxTake || action.Take > state.Pile)
        {
            return ApplyResult.Rejected<NimState>("illegal_take", "Cannot take " + action.Take + ".");
        }

        var pile = state.Pile - action.Take;
        return ApplyResult.Ok(state with { Pile = pile, ToMove = 1 - seat, Winner = pile == 0 ? seat : null });
    }

    public GameOutcome? Outcome(NimState state) =>
        state.Winner is { } winner ? GameOutcome.Winner(winner, "took_last") : null;

    public NimState ViewFor(NimState state, int seat) => state;
}

public sealed class NimFixtureAdapter : IFixtureAdapter<NimState, NimAction, NimConfig>
{
    public IGameEngine<NimState, NimAction, NimConfig> Engine { get; } = new NimEngine();

    public NimConfig ParseConfig(JsonElement? config) =>
        config?.Deserialize<NimConfig>(FixtureLoader.JsonOptions) ?? throw new InvalidDataException("Nim fixtures need a config.");

    public NimAction ParseAction(JsonElement action) =>
        action.Deserialize<NimAction>(FixtureLoader.JsonOptions) ?? throw new InvalidDataException("Bad Nim action.");

    public JsonNode ActionToJson(NimAction action) => JsonSerializer.SerializeToNode(action, FixtureLoader.JsonOptions)!;

    public JsonNode StateToJson(NimState state) => JsonSerializer.SerializeToNode(state, FixtureLoader.JsonOptions)!;

    public NimState? ParseState(JsonElement state, NimConfig config) => state.Deserialize<NimState>(FixtureLoader.JsonOptions);
}
