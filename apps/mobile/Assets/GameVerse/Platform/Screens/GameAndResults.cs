#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Core;
using GameVerse.Games.Ludo;
using GameVerse.Games.Ludo.View;
using GameVerse.Net.Realtime;
using GameVerse.Platform.Play;
using GameVerse.Platform.Shell;
using GameVerse.Rules.Ludo;
using GameVerse.UI;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Platform.Screens
{
    /// <summary>
    /// The in-game frame (DESIGN_SYSTEM screen 7) around a game module: player chips with the turn clock, a banner while
    /// reconnecting, and Leave (resign, after a confirm). Online and vs AI games run through <see cref="GameHost"/> on the
    /// server's match host; pass-and-play runs <see cref="LocalLudoHost"/> on the device. The result opens Results.
    /// </summary>
    public sealed class GameScreen : AppScreen
    {
        private readonly GameInfo game;
        private readonly SessionManifest manifest;
        private readonly RoomScreen? room;
        private readonly LudoConfig? localConfig;
        private readonly LudoModule module = new LudoModule();
        private readonly Dictionary<int, PlayerChip> chips = new Dictionary<int, PlayerChip>();
        private readonly HashSet<int> offline = new HashSet<int>();
        private readonly Label banner;
        private RealtimeConnection? connection;
        private GameHost? host;
        private LocalLudoHost? local;
        private (int? Seat, long? Deadline) turn;
        private long turnStartedAt;
        private bool ended;

        private GameScreen(AppServices app, GameInfo game, SessionManifest manifest, RoomScreen? room, LudoConfig? localConfig)
            : base(app, "Game")
        {
            this.game = game;
            this.manifest = manifest;
            this.room = room;
            this.localConfig = localConfig;
            banner = Q<Label>("banner");
            banner.style.display = DisplayStyle.None;
            Q<Label>("title").text = game.Title + (manifest.Mode == GameModes.Local ? " · Pass and play" : manifest.Mode == GameModes.VsAI ? " · vs AI" : string.Empty);
            Q<Button>("leave").clicked += ConfirmLeave;
        }

        /// <summary>A server-hosted game (online or vs AI); <paramref name="room"/> when it belongs to a private room.</summary>
        public static GameScreen Online(AppServices app, GameInfo game, SessionManifest manifest, RoomScreen? room) =>
            new GameScreen(app, game, manifest, room, null);

        public static GameScreen PassAndPlay(AppServices app, GameInfo game, int players, LudoConfig config) =>
            new GameScreen(app, game, LocalLudoHost.CreateManifest(players), null, config);

        public LudoModule Module => module;

        public override void OnEnter() => LoadAsync().Forget();

        public override bool OnBack()
        {
            ConfirmLeave();
            return true;
        }

        public override void OnExit()
        {
            module.UnloadAsync().Forget();
            if (connection != null)
            {
                App.Release(connection);
            }
        }

        public override void Tick()
        {
            if (local != null && turn.Seat != local.State.CurrentSeat)
            {
                SetTurn((local.State.CurrentSeat, null));
            }

            if (turn.Seat is { } seat && chips.TryGetValue(seat, out var chip))
            {
                float? left = null;
                if (turn.Deadline is { } deadline && deadline > turnStartedAt)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    left = (float)(deadline - now) / (deadline - turnStartedAt);
                }

                chip.SetTurn(true, left);
            }

            if (App.Dev.Autoplay)
            {
                Autoplay.Step(module);
            }
        }

        private async UniTaskVoid LoadAsync()
        {
            BuildChips();
            if (manifest.Mode == GameModes.Local)
            {
                local = new LocalLudoHost(manifest, localConfig ?? LudoConfig.Default);
                local.MatchEnded += ShowResults;
                await module.LoadAsync(manifest, local);
                Q<VisualElement>("game").Add(module.View);
                local.Attach(module);
                return;
            }

            if (manifest.Realtime is null)
            {
                App.Toast("This game has no connection details.", true);
                App.Router.Pop();
                return;
            }

            connection = App.Connect(manifest.Realtime, new TicketRequest { SessionId = manifest.SessionId });
            host = new GameHost(manifest, connection);
            host.MatchEnded += ShowResults;
            host.TurnChanged += SetTurn;
            host.PresenceChanged += presence =>
            {
                if (presence.Connected)
                {
                    offline.Remove(presence.Seat);
                }
                else
                {
                    offline.Add(presence.Seat);
                }

                UpdateChips();
            };
            host.ConnectionChanged += status =>
            {
                banner.style.display = status is ConnectionStatus.Reconnecting or ConnectionStatus.Closed ? DisplayStyle.Flex : DisplayStyle.None;
                banner.text = status == ConnectionStatus.Closed ? "This game is no longer available." : "Reconnecting...";
            };
            host.ActionRejected += error => App.Toast(error.Message, true);
            await module.LoadAsync(manifest, host);
            Q<VisualElement>("game").Add(module.View);
            host.Attach(module);
        }

        private void BuildChips()
        {
            var container = Q<VisualElement>("players");
            var colors = LudoBoard.ColorsFor(manifest.Players.Count);
            foreach (var player in manifest.Players.OrderBy(p => p.Seat))
            {
                var chip = new PlayerChip();
                chips[player.Seat] = chip;
                container.Add(chip);
            }

            UpdateChips();
        }

        private void UpdateChips()
        {
            var colors = LudoBoard.ColorsFor(manifest.Players.Count);
            foreach (var player in manifest.Players)
            {
                var you = manifest.Mode != GameModes.Local && player.Id == manifest.LocalPlayerId;
                var status = player.IsAI ? "AI" : you ? "You" : offline.Contains(player.Seat) ? "Reconnecting" : LudoPalette.Names[colors[player.Seat]];
                chips[player.Seat].Set(player.DisplayName, status, LudoPalette.Of(colors[player.Seat]), player.IsAI || manifest.Mode == GameModes.Local ? (bool?)null : !offline.Contains(player.Seat));
                chips[player.Seat].SetGlyph(new ShapeBadge(colors[player.Seat]));
            }
        }

        private void SetTurn((int? Seat, long? Deadline) next)
        {
            foreach (var chip in chips.Values)
            {
                chip.SetTurn(false, null);
            }

            if (next.Seat != turn.Seat || next.Deadline != turn.Deadline)
            {
                turnStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            turn = next;
        }

        private void ConfirmLeave()
        {
            if (ended)
            {
                return;
            }

            var sheet = new Sheet("Leave this game?");
            var note = new Label(manifest.Mode == GameModes.Local ? "The game ends for everyone on this phone." : "You resign and finish last. The others play on.");
            note.AddToClassList("gv-muted");
            var leave = new Button { text = "Resign and leave" };
            ButtonStyle.Apply(leave, ButtonVariant.Danger, ButtonSize.Md);
            var stay = new Button { text = "Keep playing" };
            ButtonStyle.Apply(stay, ButtonVariant.Ghost, ButtonSize.Md);
            leave.clicked += () =>
            {
                sheet.Close();
                ended = true;
                module.Resign();
                if (room != null)
                {
                    App.Router.PopTo(room);
                }
                else
                {
                    App.Router.Pop();
                }
            };
            stay.clicked += sheet.Close;
            sheet.Content.Add(note);
            sheet.Content.Add(leave);
            sheet.Content.Add(stay);
            sheet.Open(App.Overlay);
        }

        private void ShowResults(MatchEndedEvent result)
        {
            if (ended)
            {
                return;
            }

            ended = true;
            WaitThenShowAsync(result).Forget();
        }

        /// <summary>Lets the last move finish animating before Results slides in.</summary>
        private async UniTaskVoid WaitThenShowAsync(MatchEndedEvent result)
        {
            var until = Time.realtimeSinceStartup + 4;
            while (module.Presenting && Time.realtimeSinceStartup < until)
            {
                await UniTask.Yield(Lifetime);
            }

            await UniTask.Delay(600, cancellationToken: Lifetime);
            App.Router.Replace(new ResultsScreen(App, game, manifest, result, room));
        }
    }

    /// <summary>
    /// Results (DESIGN_SYSTEM screen 8, owned by the platform): who placed where and why, the new rating after ranked
    /// games, and the way back: the room (rematch there), the lobby (play again) or Home. Rewards arrive with Progression.
    /// </summary>
    public sealed class ResultsScreen : AppScreen
    {
        private static readonly string[] Places = { "1st", "2nd", "3rd", "4th" };

        private readonly SessionManifest manifest;
        private readonly RoomScreen? room;

        public ResultsScreen(AppServices app, GameInfo game, SessionManifest manifest, MatchEndedEvent result, RoomScreen? room)
            : base(app, "Results")
        {
            this.manifest = manifest;
            this.room = room;
            var colors = LudoBoard.ColorsFor(manifest.Players.Count);
            var local = manifest.Mode == GameModes.Local;
            var mine = manifest.Players.FirstOrDefault(p => p.Id == manifest.LocalPlayerId)?.Seat;
            var place = mine is { } seat ? result.Placements.IndexOf(seat) : -1;
            var winner = manifest.Players.FirstOrDefault(p => result.WinningSeats.Count > 0 && p.Seat == result.WinningSeats[0]);

            Q<Label>("headline").text = local ? LudoPalette.Names[colors[winner?.Seat ?? 0]] + " wins!"
                : result.WinningSeats.Contains(mine ?? -1) ? "You won!"
                : place > 0 ? "You came " + Places[Math.Min(place, 3)]
                : "Game over";
            Q<Label>("reason").text = result.Reason switch
            {
                "all_tokens_home" => "All tokens home.",
                "opponents_left" => "Everyone else left the game.",
                _ => string.Empty,
            };

            var placings = Q<VisualElement>("placings");
            for (var i = 0; i < result.Placements.Count; i++)
            {
                var player = manifest.Players.FirstOrDefault(p => p.Seat == result.Placements[i]);
                if (player is null)
                {
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList("gv-results__row");
                var placeLabel = new Label(Places[Math.Min(i, 3)]);
                placeLabel.AddToClassList("gv-results__place");
                var chip = new PlayerChip();
                chip.Set(player.DisplayName, player.IsAI ? "AI" : player.Id == manifest.LocalPlayerId && !local ? "You" : LudoPalette.Names[colors[player.Seat]], LudoPalette.Of(colors[player.Seat]), null);
                chip.SetGlyph(new ShapeBadge(colors[player.Seat]));
                row.Add(placeLabel);
                row.Add(chip);
                placings.Add(row);
            }

            var again = Q<Button>("again");
            again.text = room != null ? "Back to the room" : "Play again";
            again.clicked += () =>
            {
                if (room != null && App.Router.Contains(room))
                {
                    App.Router.PopTo(room);
                }
                else
                {
                    App.Router.Pop(); // back to the lobby, with the same mode selected
                }
            };
            Q<Button>("home").clicked += () => App.Router.ResetTo(new HomeScreen(App));

            if (result.WinningSeats.Contains(mine ?? -1) || local)
            {
                Haptics.Success();
            }

            if (manifest.Mode == GameModes.OnlineRanked)
            {
                RatingAsync(game.Id).Forget();
            }
        }

        public override void OnEnter()
        {
            if (App.Dev.Autoplay)
            {
                Debug.Log("GV_ACCEPT session=" + manifest.SessionId + " ended=" + Q<Label>("headline").text);
            }
        }

        private async UniTaskVoid RatingAsync(string gameId)
        {
            try
            {
                await UniTask.Delay(500, cancellationToken: Lifetime); // the rating is written as the result is recorded
                var rating = await App.Api.GetAsync<RatingDto>("/matchmaking/ratings/" + gameId, Lifetime);
                Q<Label>("rating").text = "Your rating is now " + rating.Rating + (rating.Provisional ? " (still settling)" : string.Empty) + ".";
            }
            catch (Exception)
            {
                // Rating display is optional.
            }
        }
    }
}
