using System.Text.Json;
using System.Text.Json.Nodes;
using GameVerse.Rules.Engine;

namespace GameVerse.Rules.Tests.Fixtures;

/// <summary>Maps a game's typed engine to and from fixture JSON. One implementation per game.</summary>
public interface IFixtureAdapter<TState, TAction, TConfig>
{
    IGameEngine<TState, TAction, TConfig> Engine { get; }

    TConfig ParseConfig(JsonElement? config);

    TAction ParseAction(JsonElement action);

    JsonNode ActionToJson(TAction action);

    JsonNode StateToJson(TState state);

    /// <summary>Builds a state from fixture JSON, for fixtures that start mid-game. Return null if unsupported.</summary>
    TState? ParseState(JsonElement state, TConfig config);
}

public sealed class FixtureMismatchException(string message) : Exception(message);

/// <summary>Replays a fixture through an engine and fails with a precise message on the first mismatch.</summary>
public static class FixtureRunner
{
    public static void Run<TState, TAction, TConfig>(IFixtureAdapter<TState, TAction, TConfig> adapter, RulesFixture fixture)
    {
        var engine = adapter.Engine;
        var config = adapter.ParseConfig(fixture.Config);
        var seats = fixture.Seats.Select(s => new SeatInfo(s.Seat, s.PlayerId, s.IsAI)).ToList();

        TState state;
        if (fixture.InitialState is { } initial)
        {
            state = adapter.ParseState(initial, config)
                ?? throw Fail(fixture, "initialState", "this game's adapter cannot build a state from JSON");
        }
        else
        {
            state = engine.NewGame(config, seats, fixture.Seed);
        }

        for (var i = 0; i < fixture.Steps.Count; i++)
        {
            var step = fixture.Steps[i];
            var where = "step " + (i + 1) + " (seat " + step.Seat + ")";

            if (step.Expect.LegalActions is { } expectedLegal)
            {
                var actual = new JsonArray(engine.LegalActions(state, step.Seat).Select(adapter.ActionToJson).ToArray());
                var expected = JsonNode.Parse(expectedLegal.GetRawText());
                if (!JsonNode.DeepEquals(actual, expected))
                {
                    throw Fail(fixture, where, "legal actions differ.\n  expected: " + Compact(expected) + "\n  actual:   " + Compact(actual));
                }
            }

            var action = adapter.ParseAction(step.Action);
            var result = engine.Apply(state, step.Seat, action);

            if (step.Expect.Ok && !result.IsOk)
            {
                throw Fail(fixture, where, "expected the action to be accepted, but it was rejected: " + result.Error);
            }

            if (!step.Expect.Ok)
            {
                if (result.IsOk)
                {
                    throw Fail(fixture, where, "expected rejection '" + step.Expect.Error + "', but the action was accepted.");
                }

                if (step.Expect.Error != null && result.Error!.Code != step.Expect.Error)
                {
                    throw Fail(fixture, where, "expected error '" + step.Expect.Error + "', got '" + result.Error!.Code + "'.");
                }
            }

            if (result.IsOk)
            {
                state = result.State;
            }

            if (step.Expect.State is { } expectedState)
            {
                var actualJson = adapter.StateToJson(state);
                var mismatch = FirstSubsetMismatch(JsonNode.Parse(expectedState.GetRawText()), actualJson, "state");
                if (mismatch != null)
                {
                    throw Fail(fixture, where, mismatch);
                }
            }
        }

        CheckOutcome(fixture, engine.Outcome(state));
    }

    private static void CheckOutcome(RulesFixture fixture, GameOutcome? outcome)
    {
        if (fixture.Outcome == null)
        {
            return;
        }

        var expected = fixture.Outcome;
        if (expected.Kind == "none")
        {
            if (outcome != null)
            {
                throw Fail(fixture, "outcome", "expected the game to still be running, but it ended (" + outcome.Kind + ", " + outcome.Reason + ").");
            }

            return;
        }

        if (outcome == null)
        {
            throw Fail(fixture, "outcome", "expected '" + expected.Kind + "', but the game is still running.");
        }

        var kind = outcome.Kind.ToString().ToLowerInvariant();
        if (kind != expected.Kind)
        {
            throw Fail(fixture, "outcome", "expected kind '" + expected.Kind + "', got '" + kind + "'.");
        }

        if (expected.WinningSeats != null && !expected.WinningSeats.SequenceEqual(outcome.WinningSeats))
        {
            throw Fail(fixture, "outcome", "expected winning seats [" + string.Join(",", expected.WinningSeats) + "], got [" + string.Join(",", outcome.WinningSeats) + "].");
        }

        if (expected.Reason != null && expected.Reason != outcome.Reason)
        {
            throw Fail(fixture, "outcome", "expected reason '" + expected.Reason + "', got '" + outcome.Reason + "'.");
        }
    }

    /// <summary>Every property in <paramref name="expected"/> must match <paramref name="actual"/>; arrays and leaves match exactly.</summary>
    private static string? FirstSubsetMismatch(JsonNode? expected, JsonNode? actual, string path)
    {
        if (expected is JsonObject expectedObject)
        {
            if (actual is not JsonObject actualObject)
            {
                return path + ": expected an object, got " + Compact(actual);
            }

            foreach (var (key, value) in expectedObject)
            {
                if (!actualObject.TryGetPropertyValue(key, out var actualValue))
                {
                    return path + "." + key + ": missing from actual state.";
                }

                var mismatch = FirstSubsetMismatch(value, actualValue, path + "." + key);
                if (mismatch != null)
                {
                    return mismatch;
                }
            }

            return null;
        }

        return JsonNode.DeepEquals(expected, actual) ? null : path + ": expected " + Compact(expected) + ", got " + Compact(actual);
    }

    private static string Compact(JsonNode? node) => node?.ToJsonString() ?? "null";

    private static FixtureMismatchException Fail(RulesFixture fixture, string where, string what) =>
        new(fixture.SourceFile + " [" + fixture.Name + "] " + where + ": " + what);
}
