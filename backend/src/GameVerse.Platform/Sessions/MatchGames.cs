using System.Text.Json;
using GameVerse.Contracts.Ludo;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using GameVerse.Rules.Random;

namespace GameVerse.Platform.Sessions;

/// <summary>The result of applying an action: the new state, or a stable rejection code.</summary>
public sealed record MatchApplyResult(object? State, string? ErrorCode, string? ErrorMessage)
{
    public bool IsOk => ErrorCode is null;
}

/// <summary>
/// One game, as the realtime match host drives it. The host is game-agnostic: it holds an opaque state and calls these
/// methods, which wrap the shared GameVerse.Rules engine. Actions and views are JSON in the game's wire format
/// (GameVerse.Contracts). Server-only: <see cref="Serialize"/> includes the dice PRNG.
/// </summary>
public interface IMatchGame
{
    string GameId { get; }

    /// <summary>A new game. House rules must already be valid (checked when the session was created).</summary>
    object NewGame(IReadOnlyDictionary<string, string> houseRules, int playerCount, ulong seed);

    MatchApplyResult Apply(object state, int seat, JsonElement action);

    /// <summary>What <paramref name="seat"/> may see, in the game's wire format (never the PRNG).</summary>
    JsonElement View(object state, int seat);

    /// <summary>The outcome once the game is over, else null. Placements map seat to place (1 = first).</summary>
    MatchOutcome? Outcome(object state);

    /// <summary>The seat whose decision the game waits for, or null when it is over.</summary>
    int? ActingSeat(object state);

    /// <summary>What the server plays for a seat whose turn clock ran out.</summary>
    JsonElement TimeoutAction(object state, int seat);

    /// <summary>An AI seat's action. <paramref name="rng"/> is the AI's own PRNG, never the dice.</summary>
    JsonElement AiAction(object state, int seat, int level, Xoshiro256StarStar rng);

    /// <summary>The action that removes a seat from play (resign, or the disconnect forfeit).</summary>
    JsonElement ForfeitAction { get; }

    /// <summary>The full state for a server snapshot, PRNG included. Never send this to a client.</summary>
    string Serialize(object state);

    object Deserialize(string snapshot);
}

/// <summary>The games the match host can run, by catalog id.</summary>
public interface IMatchGames
{
    IMatchGame? Find(string gameId);
}

internal sealed class MatchGames : IMatchGames
{
    private readonly Dictionary<string, IMatchGame> games = new(StringComparer.Ordinal) { ["ludo"] = new LudoMatchGame() };

    public IMatchGame? Find(string gameId) => games.GetValueOrDefault(gameId);
}

/// <summary>Ludo for the match host. Actions: {"roll":true}, {"move":n}, {"forfeit":true} (GameVerse.Contracts.Ludo.LudoActions).</summary>
internal sealed class LudoMatchGame : IMatchGame
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly LudoEngine Engine = new();
    private static readonly JsonElement Roll = JsonSerializer.SerializeToElement(new Dictionary<string, bool> { [LudoActions.Roll] = true });

    public string GameId => "ludo";

    public JsonElement ForfeitAction { get; } = JsonSerializer.SerializeToElement(new Dictionary<string, bool> { [LudoActions.Forfeit] = true });

    public object NewGame(IReadOnlyDictionary<string, string> houseRules, int playerCount, ulong seed)
    {
        var config = GameSetups.LudoConfigFor(houseRules, playerCount);
        var seats = Enumerable.Range(0, playerCount).Select(s => new SeatInfo(s, "seat-" + s, false)).ToList();
        return Engine.NewGame(config, seats, seed);
    }

    public MatchApplyResult Apply(object state, int seat, JsonElement action)
    {
        if (ParseAction(action) is not { } parsed)
        {
            return new MatchApplyResult(null, "bad_action", "Expected {\"roll\":true}, {\"move\":0..3} or {\"forfeit\":true}.");
        }

        var result = Engine.Apply((LudoState)state, seat, parsed);
        return result.IsOk ? new MatchApplyResult(result.State, null, null) : new MatchApplyResult(null, result.Error!.Code, result.Error.Message);
    }

    public JsonElement View(object state, int seat) =>
        JsonSerializer.SerializeToElement(GameSetups.ToDto(Engine.ViewFor((LudoState)state, seat)), Json);

    public MatchOutcome? Outcome(object state)
    {
        var outcome = Engine.Outcome((LudoState)state);
        if (outcome is null)
        {
            return null;
        }

        var kind = outcome.Kind switch
        {
            OutcomeKind.Win => "win",
            OutcomeKind.Draw => "draw",
            _ => "abandoned",
        };
        var placements = outcome.Placements.Select((seat, index) => (seat, place: index + 1)).ToDictionary(p => p.seat, p => p.place);
        return new MatchOutcome(kind, [.. outcome.WinningSeats], outcome.Reason, placements);
    }

    public int? ActingSeat(object state)
    {
        var s = (LudoState)state;
        return s.Phase == LudoPhase.Over ? null : s.CurrentSeat;
    }

    public JsonElement TimeoutAction(object state, int seat)
    {
        var s = (LudoState)state;
        if (s.Phase == LudoPhase.Roll)
        {
            return Roll;
        }

        // Level 2 is deterministic, so a timed-out move needs no PRNG and replays exactly from the move log.
        return ToJson(LudoAi.Choose(s, seat, 2, new Xoshiro256StarStar(0)));
    }

    public JsonElement AiAction(object state, int seat, int level, Xoshiro256StarStar rng) =>
        ToJson(LudoAi.Choose((LudoState)state, seat, level, rng));

    public string Serialize(object state)
    {
        var s = (LudoState)state;
        return JsonSerializer.Serialize(
            new LudoSnapshot
            {
                Config = new LudoRulesDto
                {
                    SixToLeaveBase = s.Config.SixToLeaveBase,
                    ThreeSixesForfeit = s.Config.ThreeSixesForfeit,
                    CaptureBonus = s.Config.CaptureBonus,
                    MustCapture = s.Config.MustCapture,
                    SafeSquares = s.Config.SafeSquares,
                    QuickMode = s.Config.QuickMode,
                    Placings = s.Config.Placings, // as asked: the engine caps it again on restore
                    Teams = s.Config.Teams,
                },
                Colors = [.. s.Colors],
                Positions = [.. s.Positions],
                CurrentSeat = s.CurrentSeat,
                Phase = (int)s.Phase,
                LastRoll = s.LastRoll,
                SixesInRow = s.SixesInRow,
                Finished = [.. s.Finished],
                Left = [.. s.Left],
                Rng = s.RngState is null ? null : [.. s.RngState],
                MoveNumber = s.MoveNumber,
            },
            Json);
    }

    public object Deserialize(string snapshot)
    {
        var s = JsonSerializer.Deserialize<LudoSnapshot>(snapshot, Json) ?? throw new InvalidOperationException("Empty Ludo snapshot.");
        var c = s.Config;
        var config = new LudoConfig(
            sixToLeaveBase: c.SixToLeaveBase,
            threeSixesForfeit: c.ThreeSixesForfeit,
            captureBonus: c.CaptureBonus,
            mustCapture: c.MustCapture,
            safeSquares: c.SafeSquares,
            quickMode: c.QuickMode,
            placings: c.Placings,
            teams: c.Teams);

        // The constructor validates every field, so a corrupt snapshot fails loudly instead of playing a broken game.
        return new LudoState(config, s.Colors, s.Positions, s.CurrentSeat, (LudoPhase)s.Phase, s.LastRoll, s.SixesInRow, s.Finished, s.Rng, s.MoveNumber, s.Left);
    }

    private static LudoAction? ParseAction(JsonElement action)
    {
        if (action.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        LudoAction? parsed = null;
        var properties = 0;
        foreach (var property in action.EnumerateObject())
        {
            properties++;
            parsed = property.Name switch
            {
                LudoActions.Roll when property.Value.ValueKind == JsonValueKind.True => LudoAction.Roll,
                LudoActions.Forfeit when property.Value.ValueKind == JsonValueKind.True => LudoAction.Forfeit,
                LudoActions.Move when property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var token) && token is >= 0 and < LudoBoard.TokensPerSeat => LudoAction.Move(token),
                _ => null,
            };
        }

        return properties == 1 ? parsed : null;
    }

    private static JsonElement ToJson(LudoAction action) =>
        action.IsRoll ? Roll
        : JsonSerializer.SerializeToElement(new Dictionary<string, int> { [LudoActions.Move] = action.Token });

    private sealed class LudoSnapshot
    {
        public LudoRulesDto Config { get; set; } = new();

        public int[] Colors { get; set; } = [];

        public int[] Positions { get; set; } = [];

        public int CurrentSeat { get; set; }

        public int Phase { get; set; }

        public int LastRoll { get; set; }

        public int SixesInRow { get; set; }

        public int[] Finished { get; set; } = [];

        public int[] Left { get; set; } = [];

        public ulong[]? Rng { get; set; }

        public int MoveNumber { get; set; }
    }
}
