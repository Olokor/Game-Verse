using System.Globalization;
using System.Text.Json;
using GameVerse.Contracts.Ludo;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;

namespace GameVerse.Platform.Sessions;

/// <summary>
/// The per-game part of starting a session: house-rule parsing, whether the server owns dice (commit-reveal), and the
/// initial state in the manifest. The rules themselves live in GameVerse.Rules; this only adapts them to the platform.
/// </summary>
internal static class GameSetups
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly LudoEngine Ludo = new();

    private static readonly string[] LudoFlags =
    [
        LudoHouseRules.SixToLeaveBase, LudoHouseRules.ThreeSixesForfeit, LudoHouseRules.CaptureBonus, LudoHouseRules.MustCapture,
        LudoHouseRules.SafeSquares, LudoHouseRules.QuickMode, LudoHouseRules.Teams,
    ];

    /// <summary>Whether the server rolls dice or shuffles for this game, so the session needs a committed seed.</summary>
    public static bool UsesServerRandomness(string gameId) => gameId == "ludo";

    /// <summary>Why the house rules are invalid for this game and player count, or null when they are fine.</summary>
    public static string? HouseRulesError(string gameId, IReadOnlyDictionary<string, string> rules, int playerCount)
    {
        if (gameId != "ludo")
        {
            // TODO(gameverse): each later game validates its own house rules when its engine lands (Phase 3+).
            return null;
        }

        var (_, error) = ParseLudo(rules, playerCount);
        return error;
    }

    /// <summary>The state the manifest carries, as the given seat may see it, or null for games without one yet.</summary>
    public static JsonElement? InitialState(string gameId, IReadOnlyDictionary<string, string> rules, int playerCount, ulong seed, int viewSeat)
    {
        if (gameId != "ludo")
        {
            return null;
        }

        var (config, error) = ParseLudo(rules, playerCount);
        if (config is null)
        {
            throw new InvalidOperationException("Stored Ludo house rules are invalid: " + error);
        }

        var seats = Enumerable.Range(0, playerCount).Select(s => new SeatInfo(s, "seat-" + s.ToString(CultureInfo.InvariantCulture), false)).ToList();
        var view = Ludo.ViewFor(Ludo.NewGame(config, seats, seed), viewSeat);
        return JsonSerializer.SerializeToElement(ToDto(view), Json);
    }

    /// <summary>Maps a Ludo state to its wire form. Never includes the dice PRNG.</summary>
    public static LudoStateDto ToDto(LudoState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var c = state.Config;
        return new LudoStateDto
        {
            Rules = new LudoRulesDto
            {
                SixToLeaveBase = c.SixToLeaveBase,
                ThreeSixesForfeit = c.ThreeSixesForfeit,
                CaptureBonus = c.CaptureBonus,
                MustCapture = c.MustCapture,
                SafeSquares = c.SafeSquares,
                QuickMode = c.QuickMode,
                Placings = c.Placings,
                Teams = c.Teams,
            },
            Colors = [.. state.Colors],
            Tokens = Enumerable.Range(0, state.PlayerCount)
                .Select(seat => Enumerable.Range(0, LudoBoard.TokensPerSeat).Select(t => state.TokenAt(seat, t)).ToArray()).ToArray(),
            CurrentSeat = state.CurrentSeat,
            Phase = state.Phase.ToString().ToLowerInvariant(),
            LastRoll = state.LastRoll,
            SixesInRow = state.SixesInRow,
            Finished = [.. state.Finished],
            MoveNumber = state.MoveNumber,
        };
    }

    private static (LudoConfig? Config, string? Error) ParseLudo(IReadOnlyDictionary<string, string> rules, int playerCount)
    {
        var d = LudoConfig.Default;
        var flags = new Dictionary<string, bool>
        {
            [LudoHouseRules.SixToLeaveBase] = d.SixToLeaveBase,
            [LudoHouseRules.ThreeSixesForfeit] = d.ThreeSixesForfeit,
            [LudoHouseRules.CaptureBonus] = d.CaptureBonus,
            [LudoHouseRules.MustCapture] = d.MustCapture,
            [LudoHouseRules.SafeSquares] = d.SafeSquares,
            [LudoHouseRules.QuickMode] = d.QuickMode,
            [LudoHouseRules.Teams] = d.Teams,
        };
        var placings = d.Placings;

        foreach (var (key, value) in rules)
        {
            if (LudoFlags.Contains(key))
            {
                if (!bool.TryParse(value, out var on))
                {
                    return (null, $"'{key}' must be \"true\" or \"false\".");
                }

                flags[key] = on;
            }
            else if (key == LudoHouseRules.Placings)
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out placings) || placings is < 1 or > 3)
                {
                    return (null, $"'{key}' must be 1, 2 or 3.");
                }
            }
            else
            {
                return (null, $"'{key}' is not a Ludo house rule.");
            }
        }

        if (flags[LudoHouseRules.Teams] && playerCount != 4)
        {
            return (null, "Team Ludo needs exactly 4 players.");
        }

        return (new LudoConfig(
            sixToLeaveBase: flags[LudoHouseRules.SixToLeaveBase],
            threeSixesForfeit: flags[LudoHouseRules.ThreeSixesForfeit],
            captureBonus: flags[LudoHouseRules.CaptureBonus],
            mustCapture: flags[LudoHouseRules.MustCapture],
            safeSquares: flags[LudoHouseRules.SafeSquares],
            quickMode: flags[LudoHouseRules.QuickMode],
            placings: placings,
            teams: flags[LudoHouseRules.Teams]), null);
    }
}
