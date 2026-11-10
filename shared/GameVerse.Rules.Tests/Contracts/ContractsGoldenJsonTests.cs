using System.Text.Json;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Rules.Tests.Fixtures;

namespace GameVerse.Rules.Tests.Contracts;

/// <summary>
/// The server's half of the wire-format contract with Unity. System.Text.Json (Web defaults, as the API uses) writes
/// sample Contracts messages to shared/rules-fixtures/contracts/*.json. Unity's EditMode test reads the same files with
/// Newtonsoft and must get identical objects and identical JSON back (apps/mobile/Assets/GameVerse/Tests/EditMode).
/// The samples include the awkward cases: a dictionary key with a capital, nulls, a ulong above 2^63, "isAI".
/// Regenerate after an intended wire change with GAMEVERSE_UPDATE_GOLDEN=1.
/// </summary>
public class ContractsGoldenJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Sample game types. Unity's test declares the same shapes; the golden files keep both sides honest.
    public sealed class SampleHouseRules
    {
        public bool SixToLeaveBase { get; set; }

        public int StackLimit { get; set; }
    }

    public sealed class SampleState
    {
        public List<int> Board { get; set; } = [];

        public int Turn { get; set; }

        public ulong Seed { get; set; }

        public string? LastMove { get; set; }
    }

    public sealed class SampleAction
    {
        public int Token { get; set; }

        public int Steps { get; set; }
    }

    public static TheoryData<string, object> Samples() => new()
    {
        {
            "session-manifest",
            new SessionManifest<SessionConfig<SampleHouseRules>, SampleState>
            {
                GameId = "ludo",
                GameVersion = "1.0.0",
                SessionId = "8d0f6c3e-2b7a-4c55-9a51-0e7f5d1b2c3a",
                Mode = GameModes.OnlinePrivate,
                LocalPlayerId = "p-0",
                Players =
                [
                    new PlayerSlot { Id = "p-0", DisplayName = "Moses", Seat = 0, IsAI = false, Cosmetics = { ["diceSkin"] = "dice_neon", ["Frame"] = "gold_frame" } },
                    new PlayerSlot { Id = "ai-1", DisplayName = "Bot Ada", Seat = 1, IsAI = true },
                ],
                Config = new SessionConfig<SampleHouseRules> { Variant = null, AiLevel = 3, HouseRules = new SampleHouseRules { SixToLeaveBase = true, StackLimit = 2 } },
                InitialState = new SampleState { Board = [0, 0, 5, 12], Turn = 1, Seed = ulong.MaxValue, LastMove = null },
                Realtime = new RealtimeInfo { WsUrl = "wss://api.gameverse.app/ws/play/8d0f6c3e", Ticket = "tkt_abc123" },
            }
        },
        {
            "envelope-state",
            new Envelope<StateUpdate<SampleState>>
            {
                T = MessageTypes.State,
                Seq = 42,
                SessionId = "8d0f6c3e-2b7a-4c55-9a51-0e7f5d1b2c3a",
                StateVersion = 9_007_199_254_740_993,
                Payload = new StateUpdate<SampleState> { State = new SampleState { Board = [1, 2], Turn = 0, Seed = 12345 }, AppliedSeq = null },
            }
        },
        {
            "event-action-submitted",
            new GameEvent<ActionSubmittedEvent<SampleAction>>
            {
                Type = GameEventTypes.ActionSubmitted,
                SessionId = "8d0f6c3e-2b7a-4c55-9a51-0e7f5d1b2c3a",
                Payload = new ActionSubmittedEvent<SampleAction> { Seq = 7, Action = new SampleAction { Token = 2, Steps = 6 } },
            }
        },
        {
            "event-match-ended",
            new GameEvent<MatchEndedEvent>
            {
                Type = GameEventTypes.MatchEnded,
                SessionId = "8d0f6c3e-2b7a-4c55-9a51-0e7f5d1b2c3a",
                Payload = new MatchEndedEvent { Kind = "win", WinningSeats = [1], Placements = [1, 0], Reason = "all_tokens_home" },
            }
        },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Server_json_matches_the_golden_file_unity_reads(string name, object message)
    {
        var json = JsonSerializer.Serialize(message, message.GetType(), Web).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var path = Path.Combine(FixtureLoader.FixtureDir("contracts"), name + ".json");

        if (Environment.GetEnvironmentVariable("GAMEVERSE_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        File.Exists(path).ShouldBeTrue($"{path} missing. Run with GAMEVERSE_UPDATE_GOLDEN=1 to create it.");
        File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).ShouldBe(json, $"Wire format changed for {name}. If intended, regenerate with GAMEVERSE_UPDATE_GOLDEN=1 and update Unity.");
    }
}
