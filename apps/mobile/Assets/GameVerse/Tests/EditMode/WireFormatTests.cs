using System.Collections.Generic;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameVerse.Tests
{
    /// <summary>
    /// Unity (Newtonsoft + GameVerseJson.Settings) reads the JSON the server writes (System.Text.Json Web defaults) and
    /// writes it back identically. Golden files come from shared/GameVerse.Rules.Tests/Contracts/ContractsGoldenJsonTests.cs.
    /// </summary>
    public class WireFormatTests
    {
        // Same shapes as the sample types in ContractsGoldenJsonTests.
        public sealed class SampleHouseRules
        {
            public bool SixToLeaveBase { get; set; }

            public int StackLimit { get; set; }
        }

        public sealed class SampleState
        {
            public List<int> Board { get; set; } = new List<int>();

            public int Turn { get; set; }

            public ulong Seed { get; set; }

            public string LastMove { get; set; }
        }

        public sealed class SampleAction
        {
            public int Token { get; set; }

            public int Steps { get; set; }
        }

        private static string Golden(string name)
        {
            var asset = Resources.Load<TextAsset>("Fixtures/contracts/" + name);
            Assert.That(asset, Is.Not.Null, "Golden JSON missing. Run `make rules`.");
            return asset.text;
        }

        /// <summary>Deserialize with GameVerseJson, re-serialize, and require the same JSON (property order ignored).</summary>
        private static T RoundTrip<T>(string name)
        {
            var json = Golden(name);
            var value = GameVerseJson.Deserialize<T>(json);
            var rewritten = JToken.Parse(GameVerseJson.Serialize(value));
            var original = JToken.Parse(json);
            Assert.That(JToken.DeepEquals(original, rewritten), Is.True,
                "Unity JSON differs from the server's.\nServer: " + original.ToString(Formatting.None) + "\nUnity:  " + rewritten.ToString(Formatting.None));
            return value;
        }

        [Test]
        public void Session_manifest_round_trips()
        {
            var m = RoundTrip<SessionManifest<SessionConfig<SampleHouseRules>, SampleState>>("session-manifest");
            Assert.That(m.GameId, Is.EqualTo("ludo"));
            Assert.That(m.Mode, Is.EqualTo(GameModes.OnlinePrivate));
            Assert.That(m.Players, Has.Count.EqualTo(2));
            Assert.That(m.Players[1].IsAI, Is.True, "isAI maps to IsAI");
            Assert.That(m.Players[0].Cosmetics["diceSkin"], Is.EqualTo("dice_neon"));
            Assert.That(m.Players[0].Cosmetics.ContainsKey("Frame"), Is.True, "dictionary keys are not camel-cased");
            Assert.That(m.Config.Variant, Is.Null);
            Assert.That(m.Config.AiLevel, Is.EqualTo(3));
            Assert.That(m.Config.HouseRules.SixToLeaveBase, Is.True);
            Assert.That(m.InitialState.Seed, Is.EqualTo(ulong.MaxValue), "ulong above 2^63 survives");
            Assert.That(m.InitialState.LastMove, Is.Null);
            Assert.That(m.Realtime.Ticket, Is.EqualTo("tkt_abc123"));
        }

        [Test]
        public void Base_manifest_reads_the_full_json()
        {
            var header = GameVerseJson.Deserialize<SessionManifest>(Golden("session-manifest"));
            Assert.That(header.GameId, Is.EqualTo("ludo"));
            Assert.That(header.Players[0].Seat, Is.EqualTo(0));
        }

        [Test]
        public void State_envelope_round_trips()
        {
            var e = RoundTrip<Envelope<StateUpdate<SampleState>>>("envelope-state");
            Assert.That(e.T, Is.EqualTo(MessageTypes.State));
            Assert.That(e.Seq, Is.EqualTo(42));
            Assert.That(e.StateVersion, Is.EqualTo(9_007_199_254_740_993L), "long above 2^53 survives");
            Assert.That(e.Payload.AppliedSeq, Is.Null);
            Assert.That(e.Payload.State.Board, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Action_event_round_trips()
        {
            var e = RoundTrip<GameEvent<ActionSubmittedEvent<SampleAction>>>("event-action-submitted");
            Assert.That(e.Type, Is.EqualTo(GameEventTypes.ActionSubmitted));
            Assert.That(e.Payload.Seq, Is.EqualTo(7));
            Assert.That(e.Payload.Action.Steps, Is.EqualTo(6));
        }

        [Test]
        public void Match_ended_event_round_trips()
        {
            var e = RoundTrip<GameEvent<MatchEndedEvent>>("event-match-ended");
            Assert.That(e.Payload.WinningSeats, Is.EqualTo(new[] { 1 }));
            Assert.That(e.Payload.Reason, Is.EqualTo("all_tokens_home"));
        }

        [Test]
        public void Default_newtonsoft_settings_would_break_dictionary_keys()
        {
            // Documents why GameVerseJson exists: the stock camelCase resolver renames "Frame" to "frame".
            var stock = new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() };
            var slot = new PlayerSlot { Cosmetics = new Dictionary<string, string> { ["Frame"] = "gold" } };
            StringAssert.Contains("\"frame\"", JsonConvert.SerializeObject(slot, stock));
            StringAssert.Contains("\"Frame\"", GameVerseJson.Serialize(slot));
        }
    }
}
