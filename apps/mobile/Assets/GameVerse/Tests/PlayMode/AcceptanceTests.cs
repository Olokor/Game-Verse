#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Accounts;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Contracts.Realtime;
using GameVerse.Contracts.Session;
using GameVerse.Games.Ludo.Logic;
using GameVerse.Net;
using GameVerse.Net.Http;
using GameVerse.Net.Realtime;
using GameVerse.Platform.Screens;
using GameVerse.Platform.Session;
using GameVerse.Platform.Shell;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GameVerse.Tests.PlayMode
{
    /// <summary>
    /// Prompt 1.6 acceptance, editor side (ADR-0011): the real client (every screen, the Ludo module, the realtime
    /// connection) autoplays a full private-room game through the real backend against a bot seat. The same autoplay runs
    /// on the phone in `make device-accept`. Opt-in: needs `make up` and GV_ACCEPT=1 (`make unity-accept`).
    /// </summary>
    public class AcceptanceTests
    {
        [UnityTest]
        public IEnumerator The_app_plays_a_full_private_room_game_through_the_backend()
        {
            if (Environment.GetEnvironmentVariable("GV_ACCEPT") != "1")
            {
                Assert.Ignore("Set GV_ACCEPT=1 and run `make up` (make unity-accept).");
            }

            var baseUrl = AppConfig.ResolveApiBaseUrl();
            var http = new UnityHttpTransport();

            // The bot: a guest that opens a 2-player room and readies up.
            var guest = Run(new ApiClient(baseUrl, http, null).PostAsync<GuestCreatedResponse>("/auth/guest", null));
            yield return Wait(guest);
            var bot = new ApiClient(baseUrl, http, new Token(guest.Result.AccessToken));
            var room = Run(bot.PostAsync<RoomDto>("/rooms", new CreateRoomRequest { GameId = "ludo", PlayerCount = 2 }));
            yield return Wait(room);
            yield return Wait(Run(bot.PostAsync<RoomDto>("/rooms/" + room.Result.Code + "/ready", new RoomReadyRequest { Ready = true })));

            // The app: a fresh first run told to autoplay that room, exactly like the phone's launch extra.
            foreach (var key in new[] { PlayerSession.GuestIdKey, PlayerSession.DeviceSecretKey, PlayerSession.RefreshTokenKey })
            {
                PlayerPrefs.DeleteKey("gv.dev." + key);
            }

            Environment.SetEnvironmentVariable("GV_AUTOPLAY_ROOM", room.Result.Code);
            var host = new GameObject("App");
            var document = host.AddComponent<UIDocument>();
#if UNITY_EDITOR
            document.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/GameVerse/UI/GameVersePanelSettings.asset");
#endif
            yield return null;
            document.rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>("Screens/Screens"));
            var app = new AppServices(document.rootVisualElement);
            Assert.That(app.Dev.AutoplayRoom, Is.EqualTo(room.Result.Code));
            app.Router.Push(new WelcomeScreen(app));

            try
            {
                // Wait for the app to sign in, find Ludo, join the room and ready up, then start as the host.
                RoomDto? full = null;
                var deadline = Time.realtimeSinceStartup + 90;
                while (full is null && Time.realtimeSinceStartup < deadline)
                {
                    var poll = Run(bot.GetAsync<RoomDto>("/rooms/" + room.Result.Code));
                    yield return Wait(poll, app);
                    full = poll.Result.Seats.All(s => s.PlayerId != null && s.Ready) ? poll.Result : null;
                    yield return Tick(app, 0.5f);
                }

                Assert.That(full, Is.Not.Null, "the app joined the room and readied up");
                var started = Run(bot.PostAsync<RoomDto>("/rooms/" + room.Result.Code + "/start", null));
                yield return Wait(started, app);
                var sessionId = started.Result.SessionId!;

                // The bot plays its seat with the same client library; the app autoplays through its own screens.
                var manifest = Run(bot.GetAsync<SessionManifest>("/sessions/" + sessionId + "/manifest"));
                yield return Wait(manifest, app);
                var botSeat = manifest.Result.Players.Single(p => p.Id == guest.Result.UserId).Seat;
                var botPlayer = new LudoController(new[] { botSeat });
                var connection = new RealtimeConnection(() => new NativeSocket(), ct => bot.PostAsync<RealtimeInfo>("/realtime/tickets", new TicketRequest { SessionId = sessionId }, ct), () => Time.realtimeSinceStartupAsDouble, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var nextBotMove = 0f;
                connection.StateReceived += state => botPlayer.Apply(state.State.ToObject<LudoStateDto>(GameVerseJson.Serializer)!);
                connection.Start(manifest.Result.Realtime!);

                deadline = Time.realtimeSinceStartup + 900;
                while (app.Router.Top is not ResultsScreen && Time.realtimeSinceStartup < deadline)
                {
                    connection.Tick();
                    app.Tick();
                    if (Time.realtimeSinceStartup >= nextBotMove)
                    {
                        nextBotMove = Time.realtimeSinceStartup + 0.3f;
                        var action = botPlayer.Roll() ?? (botPlayer.MovableTokens().Count > 0 ? botPlayer.Move(botPlayer.MovableTokens()[0].Seat, botPlayer.MovableTokens()[0].Token) : null);
                        if (action != null)
                        {
                            connection.SendAction(action);
                        }
                    }

                    yield return null;
                }

                connection.Dispose();
                Assert.That(app.Router.Top, Is.InstanceOf<ResultsScreen>(), "the app reached Results");
                var fairness = Run(bot.GetAsync<SessionFairness>("/sessions/" + sessionId + "/fairness"));
                yield return Wait(fairness, app);
                Assert.That(fairness.Result.Revealed, Is.True, "the server recorded the result and revealed the dice");
                Debug.Log("GV_ACCEPT editor session=" + sessionId + " room=" + room.Result.Code + " PASS");
            }
            finally
            {
                Environment.SetEnvironmentVariable("GV_AUTOPLAY_ROOM", null);
                UnityEngine.Object.Destroy(host);
            }
        }

        private static System.Threading.Tasks.Task<T> Run<T>(UniTask<T> task) => task.AsTask();

        private static IEnumerator Wait<T>(System.Threading.Tasks.Task<T> task, AppServices? app = null)
        {
            var until = Time.realtimeSinceStartup + 30;
            while (!task.IsCompleted && Time.realtimeSinceStartup < until)
            {
                app?.Tick();
                yield return null;
            }

            Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString() ?? "timed out");
        }

        private static IEnumerator Tick(AppServices app, float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                app.Tick();
                yield return null;
            }
        }

        private sealed class Token : IAccessTokenSource
        {
            public Token(string token) => AccessToken = token;

            public string? AccessToken { get; }

            public UniTask<bool> RenewAsync(CancellationToken ct) => UniTask.FromResult(false);
        }
    }
}
