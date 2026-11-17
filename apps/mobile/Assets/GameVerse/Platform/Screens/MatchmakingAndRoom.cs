#nullable enable
using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Games.Ludo.View;
using GameVerse.Net;
using GameVerse.Net.Http;
using GameVerse.Net.Realtime;
using GameVerse.Platform.Shell;
using GameVerse.UI;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Platform.Screens
{
    /// <summary>
    /// Matchmaking (DESIGN_SYSTEM screen 5): a pulsing search with the time waited and, for ranked, the current rating
    /// band. Polls GET /matchmaking/queue once a second; when matched it loads the session and opens the game.
    /// "Play vs AI while you wait" cancels the search and starts a vs AI game.
    /// </summary>
    public sealed class MatchmakingScreen : AppScreen
    {
        private readonly GameInfo game;
        private readonly int players;
        private readonly VisualElement pulse;
        private float nextPoll;
        private bool polling;
        private bool done;
        private bool leaving;

        public MatchmakingScreen(AppServices app, GameInfo game, GameModeInfo mode, int players)
            : base(app, "Matchmaking")
        {
            this.game = game;
            this.players = players;
            Q<Label>("title").text = mode.Label + " · " + players + " players";
            pulse = Q<VisualElement>("pulse");
            Q<Button>("cancel").clicked += () => CancelAsync(thenVsAi: false).Forget();
            Q<Button>("vs-ai").clicked += () => CancelAsync(thenVsAi: true).Forget();
        }

        public override bool OnBack()
        {
            CancelAsync(thenVsAi: false).Forget();
            return true;
        }

        public override void Tick()
        {
            if (!MotionPreferences.Reduced)
            {
                var phase = Time.realtimeSinceStartup % GameVerseTokens.Motion.Ambient.Duration / GameVerseTokens.Motion.Ambient.Duration;
                pulse.style.scale = new Scale(Vector3.one * (1 + (0.06f * Mathf.Sin(phase * Mathf.PI * 2))));
            }

            if (!done && !polling && Time.realtimeSinceStartup >= nextPoll)
            {
                PollAsync().Forget();
            }
        }

        private async UniTaskVoid PollAsync()
        {
            polling = true;
            nextPoll = Time.realtimeSinceStartup + 1;
            try
            {
                var status = await App.Api.GetAsync<QueueStatusDto>("/matchmaking/queue", Lifetime);
                Q<Label>("time").text = (status.WaitedSeconds / 60) + ":" + (status.WaitedSeconds % 60).ToString("00");
                Q<Label>("detail").text = status.RatingBand is { } band ? "Looking within " + band + " rating points" : "Looking for anyone";
                if (status.Status == QueueStatuses.Matched && !done)
                {
                    done = true;
                    Haptics.Medium();
                    Q<Label>("status").text = "Game found";
                    var manifest = await App.Api.GetAsync<SessionManifest>("/sessions/" + status.SessionId + "/manifest", Lifetime);
                    App.Router.Replace(GameScreen.Online(App, game, manifest, null));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ApiException ex) when (ex.Status == 404)
            {
                done = true; // not searching any more (expired or cancelled elsewhere)
                App.Router.Pop();
            }
            catch (Exception)
            {
                Q<Label>("detail").text = "Connection trouble. Still trying.";
            }
            finally
            {
                polling = false;
            }
        }

        private async UniTaskVoid CancelAsync(bool thenVsAi)
        {
            if (leaving)
            {
                return; // a second tap while the first is on its way
            }

            leaving = true;
            done = true;
            var button = Q<Button>(thenVsAi ? "vs-ai" : "cancel");
            var ok = await Busy(button, async () =>
            {
                try
                {
                    await App.Api.DeleteAsync("/matchmaking/queue", Lifetime);
                }
                catch (ApiException)
                {
                    // Already matched or gone: nothing to cancel.
                }

                if (thenVsAi && game.Modes.FirstOrDefault(m => m.Kind == ModeKinds.VsAi) is { } vsAi)
                {
                    // Create the game while this screen is still alive, then swap it in.
                    var manifest = await LobbyScreen.CreateVsAiAsync(App, game, vsAi.Key, Math.Min(players, vsAi.PlayerCounts.Max()), 2, new System.Collections.Generic.Dictionary<string, string>(), Lifetime);
                    App.Router.Replace(GameScreen.Online(App, game, manifest, null));
                    return;
                }

                App.Router.Pop();
            });
            if (!ok)
            {
                leaving = false; // failed: let the player try again
            }
        }
    }

    /// <summary>
    /// A private room (DESIGN_SYSTEM screen 6): the code to share, seats with ready states, the host's rules and Start.
    /// Follows the room live on /ws/room/{code}; when a game starts it opens it, and after the game the same ready-up is
    /// the rematch (or the next game of a best of three).
    /// </summary>
    public sealed class RoomScreen : AppScreen
    {
        private readonly GameInfo game;
        private readonly RoomCodeDisplay code = new RoomCodeDisplay();
        private readonly VisualElement seats;
        private readonly Button ready;
        private readonly Button start;
        private readonly Button rules;
        private readonly HouseRules houseRules = new HouseRules();
        private RoomDto room;
        private RealtimeConnection? lobby;
        private string? playedSession;
        private bool leaving;

        public RoomScreen(AppServices app, GameInfo game, RoomDto room)
            : base(app, "Room")
        {
            this.game = game;
            this.room = room;
            Q<VisualElement>("code-slot").Add(code);
            seats = Q<VisualElement>("seats");
            ready = Q<Button>("ready");
            start = Q<Button>("start");
            rules = Q<Button>("rules");
            Q<Button>("leave").clicked += () => LeaveAsync().Forget();
            ready.clicked += () => ToggleReadyAsync().Forget();
            start.clicked += () => Act(start, "/rooms/" + room.Code + "/start", null);
            rules.clicked += () =>
            {
                // The host's changes go to the server when the sheet closes; everyone's ready flag resets there.
                houseRules.OpenSheet(App.Overlay, this.room.PlayerCount == 4).Closed += () =>
                    Busy(rules, async () => Apply(await App.Api.PatchAsync<RoomDto>("/rooms/" + this.room.Code + "/rules", new RoomRulesRequest { HouseRules = houseRules.ToWire(this.room.PlayerCount) }, Lifetime))).Forget();
            };
            code.CopyButton.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = this.room.ShareUrl;
                App.Toast("Link copied");
            };
            code.ShareButton.clicked += () => Share("Join my " + game.Title + " room on GameVerse: " + this.room.Code + " " + this.room.ShareUrl);
        }

        public RoomDto Room => room;

        public override void OnEnter()
        {
            Render();
            FollowAsync().Forget();
        }

        public override void OnResume() => RefreshAsync().Forget();

        public override bool OnBack()
        {
            LeaveAsync().Forget();
            return true;
        }

        public override void OnExit()
        {
            if (lobby != null)
            {
                App.Release(lobby);
            }
        }

        private async UniTaskVoid FollowAsync()
        {
            try
            {
                var ticket = await App.Api.PostAsync<RealtimeInfo>("/realtime/tickets", new TicketRequest { RoomCode = room.Code }, Lifetime);
                lobby = App.Connect(ticket, new TicketRequest { RoomCode = room.Code });
                lobby.RoomReceived += json => Apply(json.ToObject<RoomDto>(GameVerseJson.Serializer)!);
            }
            catch (OperationCanceledException)
            {
                // Left the room before the socket opened.
            }
            catch (Exception)
            {
                App.Toast("Live updates are off. Leave and reopen the room to refresh.", true);
            }
        }

        private async UniTaskVoid RefreshAsync()
        {
            try
            {
                Apply(await App.Api.GetAsync<RoomDto>("/rooms/" + room.Code, Lifetime));
            }
            catch (Exception)
            {
                // The lobby socket keeps us current; a failed refresh is not worth a toast.
            }
        }

        private void Apply(RoomDto next)
        {
            room = next;
            Render();

            // A game started (by the host, or by everyone readying up after the last one): open it.
            if (room.Status == RoomStatuses.Playing && room.SessionId != null && room.SessionId != playedSession && App.Router.Top == this)
            {
                playedSession = room.SessionId;
                OpenGameAsync(room.SessionId).Forget();
            }
        }

        private async UniTaskVoid OpenGameAsync(string sessionId)
        {
            try
            {
                var manifest = await App.Api.GetAsync<SessionManifest>("/sessions/" + sessionId + "/manifest", Lifetime);
                Haptics.Medium();
                App.Router.Push(GameScreen.Online(App, game, manifest, this));
            }
            catch (Exception error)
            {
                playedSession = null;
                App.Fail(error);
            }
        }

        private void Render()
        {
            code.Code = room.Code;
            var me = room.Seats.FirstOrDefault(s => s.PlayerId == App.Session.UserId);
            var host = room.Seats.FirstOrDefault(s => s.Seat == room.HostSeat)?.PlayerId == App.Session.UserId;
            var full = room.Seats.All(s => s.PlayerId != null);
            var allReady = full && room.Seats.All(s => s.Ready);
            var after = room.Status is RoomStatuses.BetweenGames or RoomStatuses.SeriesOver;

            Q<Label>("title").text = room.BestOf == 3 ? "Best of three" : "Private room";
            Q<Label>("summary").text = Summary(after);

            seats.Clear();
            var colors = GameVerse.Rules.Ludo.LudoBoard.ColorsFor(room.PlayerCount);
            foreach (var seat in room.Seats)
            {
                var chip = new PlayerChip();
                var status = seat.PlayerId is null ? "Waiting for a player"
                    : (seat.Seat == room.HostSeat ? "Host · " : string.Empty) + (seat.Ready ? "Ready" : "Not ready") + (room.BestOf == 3 || after ? " · " + seat.Wins + " won" : string.Empty);
                chip.Set(seat.DisplayName ?? "Empty seat", status, seat.PlayerId is null ? (Color)GameVerseTokens.Color.Bg.Elevated : LudoPalette.Of(colors[seat.Seat]), seat.PlayerId is null ? (bool?)null : true);
                chip.SetGlyph(seat.PlayerId is null ? null : new ShapeBadge(colors[seat.Seat]));
                seats.Add(chip);
            }

            ready.text = me?.Ready == true ? "Not ready" : after ? (room.Status == RoomStatuses.SeriesOver ? "Rematch" : "Next game") : "I'm ready";
            ready.SetEnabled(me != null && room.Status != RoomStatuses.Playing);
            start.style.display = host && room.Status == RoomStatuses.Lobby ? DisplayStyle.Flex : DisplayStyle.None;
            start.SetEnabled(allReady);
            var missing = room.Seats.Count(s => s.PlayerId is null);
            start.text = allReady ? "Start game" : missing > 0 ? "Waiting for " + missing + (missing == 1 ? " player" : " players") : "Waiting for everyone to be ready";
            rules.style.display = host && room.Status == RoomStatuses.Lobby ? DisplayStyle.Flex : DisplayStyle.None;

            if (App.Dev.Autoplay && me is { Ready: false } && room.Status != RoomStatuses.Playing)
            {
                ToggleReadyAsync().Forget(); // acceptance check: ready up like a player would
            }
        }

        private string Summary(bool after)
        {
            var filled = room.Seats.Count(s => s.PlayerId != null);
            if (room.Status == RoomStatuses.SeriesOver)
            {
                var winners = string.Join(" and ", room.SeriesWinners.Select(s => room.Seats[s].DisplayName));
                return winners + " won the series. Everyone ready for a rematch?";
            }

            if (after)
            {
                return "Game " + room.GameNumber + " done. Ready up for the next one.";
            }

            return filled < room.PlayerCount
                ? "Share the code. " + filled + " of " + room.PlayerCount + " players are here."
                : "Everyone is here. The host starts when all are ready.";
        }

        private async UniTaskVoid ToggleReadyAsync()
        {
            var me = room.Seats.FirstOrDefault(s => s.PlayerId == App.Session.UserId);
            if (me is null)
            {
                return;
            }

            await Busy(ready, async () => Apply(await App.Api.PostAsync<RoomDto>("/rooms/" + room.Code + "/ready", new RoomReadyRequest { Ready = !me.Ready }, Lifetime)));
        }

        private void Act(Button button, string path, object? body) =>
            Busy(button, async () => Apply(await App.Api.PostAsync<RoomDto>(path, body, Lifetime))).Forget();

        private async UniTaskVoid LeaveAsync()
        {
            if (leaving)
            {
                return;
            }

            leaving = true;
            await Busy(Q<Button>("leave"), async () =>
            {
                try
                {
                    await App.Api.PostAsync<RoomDto>("/rooms/" + room.Code + "/leave", null, Lifetime);
                }
                catch (ApiException)
                {
                    // Gone already: leaving is done either way.
                }
            });
            App.Router.Pop();
        }

        private static void Share(string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND");
            intent.Call<AndroidJavaObject>("setType", "text/plain").Dispose();
            intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", text).Dispose();
            using var chooser = new AndroidJavaClass("android.content.Intent").CallStatic<AndroidJavaObject>("createChooser", intent, "Share room");
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call("startActivity", chooser);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
        }
    }
}
