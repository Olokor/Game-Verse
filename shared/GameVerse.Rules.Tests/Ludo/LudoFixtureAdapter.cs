using System.Text.Json;
using System.Text.Json.Nodes;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using GameVerse.Rules.Random;
using GameVerse.Rules.Tests.Fixtures;

namespace GameVerse.Rules.Tests.Ludo;

/// <summary>
/// Maps Ludo to the fixture JSON in shared/rules-fixtures/ludo (see its README).
/// <list type="bullet">
/// <item>config: the house-rule toggles (camelCase, defaults = LudoConfig.Default), plus the test-only "dice": the die
/// values to roll, in order. When they run out, the dice come from the seeded PRNG again.</item>
/// <item>actions: {"roll": true} or {"move": tokenIndex}.</item>
/// <item>state: tokens per seat (-1 base, 0..50 track, 51..55 home column, 56 home), currentSeat, phase, lastRoll,
/// sixesInRow, finished, colors, hasDice.</item>
/// </list>
/// One adapter per fixture: it holds that fixture's dice.
/// </summary>
public sealed class LudoFixtureAdapter : IFixtureAdapter<LudoState, LudoAction, LudoConfig>
{
    private readonly Queue<int> dice = new();

    public LudoFixtureAdapter() => Engine = new LudoEngine(() => dice.Count > 0 ? dice.Dequeue() : null);

    public IGameEngine<LudoState, LudoAction, LudoConfig> Engine { get; }

    /// <summary>Scripted dice the fixture listed but never rolled (a fixture mistake when non-zero).</summary>
    public int UnusedDice => dice.Count;

    public LudoConfig ParseConfig(JsonElement? config)
    {
        var d = LudoConfig.Default;
        if (config is not { } c)
        {
            return d;
        }

        if (c.TryGetProperty("dice", out var values))
        {
            foreach (var value in values.EnumerateArray())
            {
                dice.Enqueue(value.GetInt32());
            }
        }

        bool Flag(string name, bool fallback) => c.TryGetProperty(name, out var v) ? v.GetBoolean() : fallback;
        return new LudoConfig(
            sixToLeaveBase: Flag("sixToLeaveBase", d.SixToLeaveBase),
            threeSixesForfeit: Flag("threeSixesForfeit", d.ThreeSixesForfeit),
            captureBonus: Flag("captureBonus", d.CaptureBonus),
            mustCapture: Flag("mustCapture", d.MustCapture),
            safeSquares: Flag("safeSquares", d.SafeSquares),
            quickMode: Flag("quickMode", d.QuickMode),
            placings: c.TryGetProperty("placings", out var p) ? p.GetInt32() : d.Placings,
            teams: Flag("teams", d.Teams));
    }

    public LudoAction ParseAction(JsonElement action)
    {
        if (action.TryGetProperty("roll", out var roll) && roll.GetBoolean())
        {
            return LudoAction.Roll;
        }

        if (action.TryGetProperty("forfeit", out var forfeit) && forfeit.GetBoolean())
        {
            return LudoAction.Forfeit;
        }

        if (action.TryGetProperty("move", out var token))
        {
            return LudoAction.Move(token.GetInt32());
        }

        throw new InvalidDataException("A Ludo action is {\"roll\": true}, {\"move\": token} or {\"forfeit\": true}.");
    }

    public JsonNode ActionToJson(LudoAction action) =>
        action.IsRoll ? new JsonObject { ["roll"] = true }
        : action.IsForfeit ? new JsonObject { ["forfeit"] = true }
        : new JsonObject { ["move"] = action.Token };

    public JsonNode StateToJson(LudoState state)
    {
        var tokens = new JsonArray();
        for (var seat = 0; seat < state.PlayerCount; seat++)
        {
            tokens.Add(new JsonArray(Enumerable.Range(0, LudoBoard.TokensPerSeat).Select(t => (JsonNode?)state.TokenAt(seat, t)).ToArray()));
        }

        return new JsonObject
        {
            ["tokens"] = tokens,
            ["currentSeat"] = state.CurrentSeat,
            ["phase"] = state.Phase.ToString().ToLowerInvariant(),
            ["lastRoll"] = state.LastRoll,
            ["sixesInRow"] = state.SixesInRow,
            ["finished"] = new JsonArray(state.Finished.Select(s => (JsonNode?)s).ToArray()),
            ["left"] = new JsonArray(state.Left.Select(s => (JsonNode?)s).ToArray()),
            ["colors"] = new JsonArray(state.Colors.Select(c => (JsonNode?)c).ToArray()),
            ["hasDice"] = state.RngState != null,
            ["moveNumber"] = state.MoveNumber,
        };
    }

    /// <summary>A mid-game start: tokens and turn from JSON; the PRNG starts from seed 1 (fixtures script their dice).</summary>
    public LudoState? ParseState(JsonElement state, LudoConfig config)
    {
        var seats = state.GetProperty("tokens").EnumerateArray().Select(s => s.EnumerateArray().Select(t => t.GetInt32()).ToArray()).ToList();
        var phase = state.TryGetProperty("phase", out var ph) ? Enum.Parse<LudoPhase>(ph.GetString()!, ignoreCase: true) : LudoPhase.Roll;
        int Int(string name) => state.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
        var finished = state.TryGetProperty("finished", out var f) ? f.EnumerateArray().Select(x => x.GetInt32()).ToArray() : [];
        var left = state.TryGetProperty("left", out var l) ? l.EnumerateArray().Select(x => x.GetInt32()).ToArray() : [];
        return new LudoState(
            config,
            LudoBoard.ColorsFor(seats.Count),
            seats.SelectMany(s => s).ToArray(),
            Int("currentSeat"),
            phase,
            Int("lastRoll"),
            Int("sixesInRow"),
            finished,
            new Xoshiro256StarStar(1).GetState(),
            0,
            left);
    }
}
