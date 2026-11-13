#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Session;
using GameVerse.Core;
using GameVerse.Games.Ludo.Logic;
using GameVerse.Games.Ludo.View;
using GameVerse.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo
{
    /// <summary>
    /// Ludo in Unity (CLAUDE.md 4.2): presentation and input only. Every state comes from the host (the server's match
    /// host online and vs AI, <see cref="LocalLudoHost"/> in pass-and-play) and is shown in order, one animation at a
    /// time. Legal moves are asked of the shared engine; the rules are never written here.
    /// </summary>
    public sealed class LudoModule : IGameModule, IGameView, IResignable
    {
        public const string Id = "ludo";
        public const string Version = "0.1.0";

        private readonly Queue<GameStateEnvelope> incoming = new Queue<GameStateEnvelope>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly VisualElement root = new VisualElement { name = "ludo-root" };

        private SessionManifest manifest = null!;
        private IGameHost host = null!;
        private LudoController controller = null!;
        private LudoView view = null!;
        private LudoSounds? sounds;
        private bool presenting;
        private bool setUp;
        private bool passAndPlay;
        private int? lastSeatShown;
        private long submitted;

        public string GameId => Id;

        public VisualElement View => root;

        /// <summary>The controller, for tests and the autoplay acceptance check.</summary>
        public LudoController Controller => controller;

        /// <summary>True while a state is being animated: input waits until the board is still.</summary>
        public bool Presenting => presenting || incoming.Count > 0;

        public UniTask LoadAsync(SessionManifest sessionManifest, IGameHost gameHost)
        {
            manifest = sessionManifest;
            host = gameHost;
            passAndPlay = manifest.Mode == GameModes.Local;
            var localSeats = passAndPlay
                ? manifest.Players.Where(p => !p.IsAI).Select(p => p.Seat).ToList()
                : manifest.Players.Where(p => p.Id == manifest.LocalPlayerId).Select(p => p.Seat).ToList();
            controller = new LudoController(localSeats);

            sounds = new LudoSounds();
            view = new LudoView(sounds);
            if (Resources.Load<StyleSheet>("Ludo/Ludo") is { } sheet)
            {
                root.styleSheets.Add(sheet);
            }

            root.style.flexGrow = 1;
            root.Add(view);
            view.RollTapped += () => Submit(controller.Roll());
            view.TokenTapped += (seat, token) => Submit(controller.Move(seat, token));

            host.Ready(Version);
            return UniTask.CompletedTask;
        }

        public void ApplyServerState(GameStateEnvelope envelope)
        {
            incoming.Enqueue(envelope);
            if (!presenting)
            {
                PresentAllAsync().Forget();
            }
        }

        /// <summary>
        /// Development autoplay (the device acceptance check): takes the same action a player's tap would, through the same
        /// controller and host, once the board is still. Roll when allowed, otherwise the first legal token.
        /// </summary>
        public void AutoplayStep()
        {
            if (controller is null || Presenting || controller.InFlight)
            {
                return;
            }

            if (controller.CanRoll)
            {
                Submit(controller.Roll());
            }
            else if (controller.MovableTokens() is { Count: > 0 } movable)
            {
                Submit(controller.Move(movable[0].Seat, movable[0].Token));
            }
        }

        /// <summary>Gives up the game (allowed at any time; the server ranks a leaver last).</summary>
        public void Resign() => Submit(controller.Forfeit());

        public UniTask UnloadAsync()
        {
            lifetime.Cancel();
            lifetime.Dispose();
            sounds?.Dispose();
            root.RemoveFromHierarchy();
            return UniTask.CompletedTask;
        }

        private async UniTaskVoid PresentAllAsync()
        {
            presenting = true;
            try
            {
                while (incoming.Count > 0)
                {
                    var envelope = incoming.Dequeue();
                    var state = GameVerseJson.Deserialize<LudoStateDto>(envelope.StateJson);
                    if (!setUp)
                    {
                        view.Setup(state);
                        setUp = true;
                    }

                    var transition = controller.Apply(state);
                    view.SetInput(false, new (int, int)[0], null, Prompt());
                    await view.PresentAsync(state, transition, lifetime.Token);
                    host.StateApplied(envelope.StateVersion);
                    if (state.Phase == "over" && controller.Current?.Finished.Length > 0 && manifest.Players.Any(p => p.Seat == state.Finished[0] && (passAndPlay || p.Id == manifest.LocalPlayerId)))
                    {
                        view.Win();
                    }
                }

                // Pass-and-play: a curtain between players, so nobody plays with the wrong colour.
                if (passAndPlay && controller.LocalSeatToAct is { } seat && seat != lastSeatShown && controller.Current!.Phase == "roll"
                    && manifest.Players.Count(p => !p.IsAI) > 1 && lastSeatShown != null)
                {
                    await view.PassToAsync(controller.Current.Colors[seat], lifetime.Token);
                }

                lastSeatShown = controller.LocalSeatToAct ?? lastSeatShown;
            }
            catch (System.OperationCanceledException)
            {
                return; // unloading
            }
            finally
            {
                presenting = false;
            }

            if (incoming.Count > 0)
            {
                PresentAllAsync().Forget(); // something arrived during the curtain
                return;
            }

            view.SetInput(controller.CanRoll, controller.MovableTokens(), controller.LocalSeatToAct ?? controller.Current?.CurrentSeat, Prompt());
        }

        private void Submit(Dictionary<string, object>? action)
        {
            if (action is null || Presenting)
            {
                return;
            }

            view.SetInput(false, new (int, int)[0], null, Prompt());
            host.SubmitAction(++submitted, action);
        }

        private string Prompt()
        {
            var state = controller.Current;
            if (state is null)
            {
                return "Setting up the board";
            }

            if (state.Phase == "over")
            {
                return "Game over";
            }

            var name = passAndPlay ? LudoPalette.Names[state.Colors[state.CurrentSeat]] : "Your turn";
            if (controller.LocalSeatToAct != null)
            {
                return state.Phase == "roll" ? name + ". Tap the die." : "Rolled " + state.LastRoll + ". Pick a token.";
            }

            var other = manifest.Players.FirstOrDefault(p => p.Seat == state.CurrentSeat)?.DisplayName ?? LudoPalette.Names[state.Colors[state.CurrentSeat]];
            return "Waiting for " + other;
        }
    }
}
