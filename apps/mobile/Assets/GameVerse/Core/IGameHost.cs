using GameVerse.Contracts.Events;
using GameVerse.Contracts.Session;

namespace GameVerse.Core
{
    /// <summary>
    /// The platform side of the game integration contract (CLAUDE.md 4.2). Games report everything as GameEvents
    /// (ready, action_submitted, state_applied, match_ended, player_left, error, telemetry) with the same schema the
    /// web bridge uses. The platform decides what happens next (send to server, show results, grant rewards).
    /// </summary>
    public interface IGameHost
    {
        /// <summary>The manifest this session was started with.</summary>
        SessionManifest Manifest { get; }

        /// <summary>Emit an event to the platform. <see cref="GameEventHeader.SessionId"/> is filled in by the host if empty.</summary>
        /// <typeparam name="TPayload">Payload type for the event's <see cref="GameEventHeader.Type"/>.</typeparam>
        void Emit<TPayload>(GameEvent<TPayload> gameEvent);
    }

    /// <summary>Typed helpers so games don't hand-build event headers.</summary>
    public static class GameHostExtensions
    {
        public static void Ready(this IGameHost host, string gameVersion) =>
            host.Emit(Create(host, GameEventTypes.Ready, new ReadyEvent { GameVersion = gameVersion }));

        public static void SubmitAction<TAction>(this IGameHost host, long seq, TAction action) =>
            host.Emit(Create(host, GameEventTypes.ActionSubmitted, new ActionSubmittedEvent<TAction> { Seq = seq, Action = action }));

        public static void StateApplied(this IGameHost host, long stateVersion) =>
            host.Emit(Create(host, GameEventTypes.StateApplied, new StateAppliedEvent { StateVersion = stateVersion }));

        public static void MatchEnded(this IGameHost host, MatchEndedEvent result) =>
            host.Emit(Create(host, GameEventTypes.MatchEnded, result));

        public static void PlayerLeft(this IGameHost host, PlayerLeftEvent left) =>
            host.Emit(Create(host, GameEventTypes.PlayerLeft, left));

        public static void Error(this IGameHost host, string code, string message, bool fatal) =>
            host.Emit(Create(host, GameEventTypes.Error, new GameErrorEvent { Code = code, Message = message, Fatal = fatal }));

        public static void Telemetry(this IGameHost host, TelemetryEvent telemetry) =>
            host.Emit(Create(host, GameEventTypes.Telemetry, telemetry));

        private static GameEvent<TPayload> Create<TPayload>(IGameHost host, string type, TPayload payload) =>
            new GameEvent<TPayload> { Type = type, SessionId = host.Manifest.SessionId, Payload = payload };
    }
}
