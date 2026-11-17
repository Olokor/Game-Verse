#nullable enable
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Session;
using GameVerse.Net.Http;
using GameVerse.Net.Realtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameVerse.Tests
{
    public class RealtimeConnectionTests
    {
        private const string Session = "01a1d1c3-a815-787d-a7b2-642eb2f9afa7";

        private readonly List<FakeSocket> sockets = new List<FakeSocket>();
        private readonly List<RealtimeState> states = new List<RealtimeState>();
        private double now;
        private int ticketsFetched;
        private bool sessionGone;

        [SetUp]
        public void Reset()
        {
            // NUnit reuses one fixture instance for every test in the class.
            sockets.Clear();
            states.Clear();
            now = 0;
            ticketsFetched = 0;
            sessionGone = false;
        }

        [Test]
        public void It_connects_with_the_ticket_and_answers_pings()
        {
            var connection = Start();

            Assert.That(sockets[0].Url, Is.EqualTo($"ws://api.test/ws/play/{Session}?ticket=t%2B1"));
            sockets[0].Open();
            sockets[0].Receive("{\"t\":\"ping\",\"seq\":1,\"sessionId\":\"" + Session + "\",\"stateVersion\":null,\"payload\":null}");
            connection.Tick();

            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Open));
            Assert.That(JObject.Parse(sockets[0].Sent.Single())["t"]!.ToString(), Is.EqualTo("pong"));
        }

        [Test]
        public void Stale_and_repeated_states_are_dropped_but_a_snapshot_of_the_same_version_is_kept()
        {
            var connection = Opened();

            Receive(connection, State("state", 2));
            Receive(connection, State("state", 1)); // older
            Receive(connection, State("state", 2)); // the same again
            Receive(connection, State("snapshot", 2)); // a resync answer
            Receive(connection, State("state", 3));

            Assert.That(states.Select(s => (s.Kind, s.StateVersion)), Is.EqualTo(new[] { ("state", 2L), ("snapshot", 2L), ("state", 3L) }));
            Assert.That(states.Last().TurnSeat, Is.EqualTo(1));
            Assert.That(states.Last().TurnDeadline, Is.EqualTo(1794484800000));
        }

        [Test]
        public void An_unacknowledged_action_is_resent_with_the_same_seq_after_a_reconnect()
        {
            var connection = Opened();
            var seq = connection.SendAction(new { roll = true });
            Assert.That(seq, Is.EqualTo(1_000_001), "seqs continue from the base, never restart at 1");
            var first = sockets[0].Sent.Single();

            sockets[0].Drop();
            connection.Tick();
            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Reconnecting));

            now += 0.6; // first backoff step is 0.5 s
            connection.Tick();
            Assert.That(ticketsFetched, Is.EqualTo(1));
            sockets[1].Open();
            connection.Tick();

            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Open));
            Assert.That(sockets[1].Sent.Single(), Is.EqualTo(first));

            Receive(connection, State("state", 1, appliedSeq: seq));
            Assert.That(connection.PendingActions, Is.EqualTo(0));
        }

        [Test]
        public void An_error_for_an_action_settles_it()
        {
            var connection = Opened();
            var seq = connection.SendAction(new { move = 9 });

            Receive(connection, "{\"t\":\"error\",\"seq\":3,\"sessionId\":\"" + Session + "\",\"stateVersion\":null,\"payload\":{\"code\":\"bad_action\",\"message\":\"no\",\"seq\":" + seq + "}}");

            Assert.That(connection.PendingActions, Is.EqualTo(0));
        }

        [Test]
        public void Silence_past_45_seconds_counts_as_a_lost_connection()
        {
            var connection = Opened();

            now += 46;
            connection.Tick();

            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Reconnecting));
            Assert.That(sockets[0].Closed, Is.True);
        }

        [Test]
        public void A_session_the_server_no_longer_knows_stops_the_reconnects()
        {
            var connection = Opened();
            sessionGone = true;

            sockets[0].Drop();
            connection.Tick();
            now += 1;
            connection.Tick();

            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Closed));
        }

        private RealtimeConnection Start()
        {
            var connection = new RealtimeConnection(
                () =>
                {
                    var socket = new FakeSocket();
                    sockets.Add(socket);
                    return socket;
                },
                _ =>
                {
                    ticketsFetched++;
                    return sessionGone
                        ? throw ApiException.FromResponse(404, null)
                        : UniTask.FromResult(new RealtimeInfo { WsUrl = $"ws://api.test/ws/play/{Session}", Ticket = "t" + ticketsFetched });
                },
                () => now,
                seqBase: 1_000_000);
            connection.StateReceived += states.Add;
            connection.Start(new RealtimeInfo { WsUrl = $"ws://api.test/ws/play/{Session}", Ticket = "t+1" });
            return connection;
        }

        private RealtimeConnection Opened()
        {
            var connection = Start();
            sockets[0].Open();
            connection.Tick();
            return connection;
        }

        private void Receive(RealtimeConnection connection, string text)
        {
            sockets.Last().Receive(text);
            connection.Tick();
        }

        private static string State(string kind, long version, long? appliedSeq = null) =>
            "{\"t\":\"" + kind + "\",\"seq\":5,\"sessionId\":\"" + Session + "\",\"stateVersion\":" + version
            + ",\"payload\":{\"state\":{\"phase\":\"roll\"},\"appliedSeq\":" + (appliedSeq?.ToString() ?? "null") + ",\"turnSeat\":1,\"turnDeadline\":1794484800000}}";
    }
}
