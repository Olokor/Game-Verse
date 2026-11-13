#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Session;
using GameVerse.Core;
using GameVerse.Games.Ludo.Logic;
using GameVerse.Net;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using Newtonsoft.Json.Linq;

namespace GameVerse.Games.Ludo
{
    /// <summary>
    /// Pass-and-play (ADR-0008): the shared engine runs on this device, seeded from the device's secure random generator,
    /// and feeds <see cref="LudoModule"/> through the same <see cref="IGameModule.ApplyServerState"/> path the server uses.
    /// Works offline: no account, no session, no rewards.
    /// </summary>
    public sealed class LocalLudoHost : IGameHost
    {
        private static readonly LudoEngine Engine = new LudoEngine();

        private IGameModule? module;
        private LudoState state;
        private long version;

        public LocalLudoHost(SessionManifest manifest, LudoConfig config, ulong? seed = null)
        {
            Manifest = manifest;
            var seats = new List<SeatInfo>();
            foreach (var player in manifest.Players)
            {
                seats.Add(new SeatInfo(player.Seat, player.Id, false));
            }

            state = Engine.NewGame(config, seats, seed ?? RandomSeed());
        }

        public SessionManifest Manifest { get; }

        /// <summary>The game is over; the platform shows Results.</summary>
        public event Action<MatchEndedEvent>? MatchEnded;

        /// <summary>The engine state, PRNG included (this device is the authority here).</summary>
        public LudoState State => state;

        /// <summary>A pass-and-play manifest: every seat is a local player.</summary>
        public static SessionManifest CreateManifest(int playerCount)
        {
            var manifest = new SessionManifest
            {
                GameId = LudoModule.Id,
                GameVersion = LudoModule.Version,
                SessionId = "local-" + Guid.NewGuid().ToString("N"),
                Mode = GameModes.Local,
                LocalPlayerId = "local-0",
            };
            for (var seat = 0; seat < playerCount; seat++)
            {
                manifest.Players.Add(new PlayerSlot { Id = "local-" + seat, DisplayName = "Player " + (seat + 1), Seat = seat });
            }

            return manifest;
        }

        /// <summary>Connects the loaded game and shows the first state.</summary>
        public void Attach(IGameModule game)
        {
            module = game;
            Push("snapshot");
        }

        public void Emit<TPayload>(GameEvent<TPayload> gameEvent)
        {
            if (gameEvent.Type != GameEventTypes.ActionSubmitted || module is null)
            {
                return;
            }

            var payload = JToken.FromObject(gameEvent.Payload!, GameVerseJson.Serializer);
            var action = Parse(payload["action"]);
            if (action is null)
            {
                return;
            }

            // A resign in pass-and-play ends it for the player whose turn it is.
            var result = Engine.Apply(state, state.CurrentSeat, action);
            if (!result.IsOk)
            {
                Push("snapshot"); // re-enables the module's input, as a server rejection would
                return;
            }

            state = result.State;
            version++;
            Push("state");
            if (Engine.Outcome(state) is { } outcome)
            {
                MatchEnded?.Invoke(new MatchEndedEvent
                {
                    Kind = outcome.Kind == OutcomeKind.Win ? "win" : outcome.Kind == OutcomeKind.Draw ? "draw" : "abandoned",
                    WinningSeats = new List<int>(outcome.WinningSeats),
                    Placements = new List<int>(outcome.Placements),
                    Reason = outcome.Reason,
                });
            }
        }

        private void Push(string kind)
        {
            var view = LudoStateMapper.ToDto(Engine.ViewFor(state, state.CurrentSeat));
            module!.ApplyServerState(new GameStateEnvelope(kind, version, null, GameVerseJson.Serialize(view)));
        }

        private static LudoAction? Parse(JToken? action)
        {
            if (action is not JObject json)
            {
                return null;
            }

            if (json.Value<bool?>(LudoActions.Roll) == true)
            {
                return LudoAction.Roll;
            }

            if (json.Value<bool?>(LudoActions.Forfeit) == true)
            {
                return LudoAction.Forfeit;
            }

            return json.Value<int?>(LudoActions.Move) is { } token ? LudoAction.Move(token) : null;
        }

        private static ulong RandomSeed()
        {
            var bytes = new byte[8];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return BitConverter.ToUInt64(bytes, 0);
        }
    }
}
