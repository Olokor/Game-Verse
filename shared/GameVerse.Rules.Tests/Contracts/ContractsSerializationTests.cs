using System.Text.Json;
using System.Text.Json.Nodes;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;

namespace GameVerse.Rules.Tests.Contracts;

// Server-side (System.Text.Json) half of the wire-format check. Unity's Newtonsoft half is Prompt 0.6.
public class ContractsSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public sealed class TestHouseRules
    {
        public bool SixToLeaveBase { get; set; }
    }

    [Fact]
    public void Manifest_uses_the_documented_wire_names()
    {
        var manifest = new SessionManifest<SessionConfig<TestHouseRules>, object>
        {
            GameId = "ludo",
            GameVersion = "1.0.0",
            SessionId = "s-1",
            Mode = GameModes.OnlinePrivate,
            LocalPlayerId = "p-0",
            Players = [new PlayerSlot { Id = "p-0", DisplayName = "Wisdom", Seat = 0, IsAI = false, Cosmetics = { ["diceSkin"] = "dice_neon" } }],
            Config = new SessionConfig<TestHouseRules> { Variant = "classic", AiLevel = 3, HouseRules = new TestHouseRules { SixToLeaveBase = true } },
            Realtime = new RealtimeInfo { WsUrl = "wss://x/ws/play/s-1", Ticket = "t" },
        };

        var json = JsonSerializer.SerializeToNode(manifest, Web)!.AsObject();

        json.Select(p => p.Key).ShouldBe(
            ["config", "initialState", "gameId", "gameVersion", "sessionId", "mode", "localPlayerId", "players", "realtime"],
            ignoreOrder: true);
        json["mode"]!.GetValue<string>().ShouldBe("online_private");
        json["players"]![0]!["isAI"]!.GetValue<bool>().ShouldBeFalse();
        json["players"]![0]!["cosmetics"]!["diceSkin"]!.GetValue<string>().ShouldBe("dice_neon");
        json["config"]!["houseRules"]!["sixToLeaveBase"]!.GetValue<bool>().ShouldBeTrue();
        json["config"]!["aiLevel"]!.GetValue<int>().ShouldBe(3);
        json["realtime"]!["wsUrl"]!.GetValue<string>().ShouldBe("wss://x/ws/play/s-1");
    }

    [Fact]
    public void Shell_can_read_the_base_manifest_from_full_json()
    {
        const string json = """
            { "gameId": "ludo", "gameVersion": "1.0.0", "sessionId": "s", "mode": "vs_ai", "localPlayerId": "p",
              "players": [ { "id": "p", "displayName": "A", "seat": 0, "isAI": false, "cosmetics": {} } ],
              "config": { "variant": null, "houseRules": { "anything": 1 }, "aiLevel": 2 },
              "initialState": { "board": [1, 2, 3] } }
            """;

        var header = JsonSerializer.Deserialize<SessionManifest>(json, Web)!;
        header.GameId.ShouldBe("ludo");
        header.Mode.ShouldBe(GameModes.VsAI);
        header.Players.Single().Seat.ShouldBe(0);
        header.Realtime.ShouldBeNull();
    }

    [Fact]
    public void Envelope_round_trips_with_short_type_field()
    {
        var envelope = new Envelope<StateUpdate<int[]>>
        {
            T = MessageTypes.State,
            Seq = 42,
            SessionId = "s",
            StateVersion = 7,
            Payload = new StateUpdate<int[]> { State = [1, 2], AppliedSeq = 41 },
        };

        var text = JsonSerializer.Serialize(envelope, Web);
        var node = JsonNode.Parse(text)!;
        node["t"]!.GetValue<string>().ShouldBe("state");
        node["stateVersion"]!.GetValue<long>().ShouldBe(7);

        JsonSerializer.Deserialize<EnvelopeHeader>(text, Web)!.T.ShouldBe(MessageTypes.State);
        var back = JsonSerializer.Deserialize<Envelope<StateUpdate<int[]>>>(text, Web)!;
        back.Payload.State.ShouldBe([1, 2]);
        back.Payload.AppliedSeq.ShouldBe(41);
    }

    [Fact]
    public void Game_events_serialize_with_snake_case_type_values()
    {
        var ended = new GameEvent<MatchEndedEvent>
        {
            Type = GameEventTypes.MatchEnded,
            SessionId = "s",
            Payload = new MatchEndedEvent { Kind = "win", WinningSeats = [1], Reason = "all_tokens_home" },
        };

        var node = JsonSerializer.SerializeToNode(ended, Web)!;
        node["type"]!.GetValue<string>().ShouldBe("match_ended");
        node["payload"]!["winningSeats"]![0]!.GetValue<int>().ShouldBe(1);

        new[]
        {
            GameEventTypes.Ready, GameEventTypes.ActionSubmitted, GameEventTypes.StateApplied, GameEventTypes.MatchEnded,
            GameEventTypes.PlayerLeft, GameEventTypes.Error, GameEventTypes.Telemetry,
        }.ShouldBe(["ready", "action_submitted", "state_applied", "match_ended", "player_left", "error", "telemetry"]);
    }
}
