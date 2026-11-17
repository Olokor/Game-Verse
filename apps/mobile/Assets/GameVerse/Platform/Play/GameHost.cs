#nullable enable
using System;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Core;
using GameVerse.Net;
using GameVerse.Net.Realtime;
using Newtonsoft.Json.Linq;

namespace GameVerse.Platform.Play
{
    /// <summary>
    /// The platform side of a server-hosted game (online, vs AI): an <see cref="IGameHost"/> over a
    /// <see cref="RealtimeConnection"/> (CLAUDE.md 4.2, 4.5). Games emit events; the host turns <c>action_submitted</c> into
    /// an <c>action</c> envelope and hands every accepted state back to the game. Turn clocks, presence and the result go to
    /// the platform UI (HUD, Results), never into the game.
    /// <para>
    /// The wire seq is the connection's, not the game's: a game's own <c>ActionSubmittedEvent.Seq</c> is not sent. A
    /// rejected action is answered with a fresh snapshot, so a game can simply re-enable input on the next state it gets.
    /// </para>
    /// </summary>
    public sealed class GameHost : IGameHost, IDisposable
    {
        private readonly RealtimeConnection connection;
        private IGameModule? module;

        public GameHost(SessionManifest manifest, RealtimeConnection connection)
        {
            Manifest = manifest;
            this.connection = connection;
            connection.StateReceived += OnState;
            connection.PresenceReceived += presence => PresenceChanged?.Invoke(presence);
            connection.Ended += result => MatchEnded?.Invoke(result);
            connection.ErrorReceived += OnError;
            connection.StatusChanged += status => ConnectionChanged?.Invoke(status);
        }

        public SessionManifest Manifest { get; }

        public RealtimeConnection Connection => connection;

        /// <summary>The latest turn clock: whose decision, and when the server plays for them (Unix ms).</summary>
        public (int? Seat, long? Deadline) Turn { get; private set; }

        /// <summary>The game said it is ready to receive state.</summary>
        public event Action? Ready;

        public event Action<(int? Seat, long? Deadline)>? TurnChanged;

        public event Action<PresencePayload>? PresenceChanged;

        public event Action<MatchEndedEvent>? MatchEnded;

        public event Action<ConnectionStatus>? ConnectionChanged;

        /// <summary>An action the server refused (stale input, rule error); shown as a toast.</summary>
        public event Action<ErrorPayload>? ActionRejected;

        /// <summary>Events the platform does not consume itself (telemetry, game errors), for logging.</summary>
        public event Action<GameEventHeader>? OtherEvent;

        /// <summary>Connects a loaded game. States arriving before this are replayed from the next snapshot.</summary>
        public void Attach(IGameModule game)
        {
            module = game;
            if (connection.StateVersion >= 0)
            {
                connection.Resync();
            }
        }

        public void Emit<TPayload>(GameEvent<TPayload> gameEvent)
        {
            switch (gameEvent.Type)
            {
                case GameEventTypes.Ready:
                    Ready?.Invoke();
                    break;
                case GameEventTypes.ActionSubmitted:
                    // ActionSubmittedEvent<TAction>: only the action goes on the wire, in the game's JSON shape.
                    var payload = JToken.FromObject(gameEvent.Payload!, GameVerseJson.Serializer);
                    if (payload["action"] is { } action)
                    {
                        connection.SendAction(action);
                    }

                    break;
                default:
                    OtherEvent?.Invoke(gameEvent);
                    break;
            }
        }

        public void Dispose() => connection.Dispose();

        private void OnState(RealtimeState state)
        {
            Turn = (state.TurnSeat, state.TurnDeadline);
            TurnChanged?.Invoke(Turn);
            module?.ApplyServerState(new GameStateEnvelope(state.Kind, state.StateVersion, state.AppliedSeq, state.State.ToString(Newtonsoft.Json.Formatting.None)));
        }

        private void OnError(ErrorPayload error)
        {
            ActionRejected?.Invoke(error);
            if (error.Seq is > 0)
            {
                connection.Resync(); // the game re-enables its input on the snapshot
            }
        }
    }
}
