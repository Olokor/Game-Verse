#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Games.Ludo;
using GameVerse.Platform.Screens;
using GameVerse.Platform.Session;
using GameVerse.Platform.Shell;
using GameVerse.Rules.Ludo;
using GameVerse.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GameVerse.Tests.PlayMode
{
    /// <summary>
    /// Renders every platform screen at the 360 x 800 design size (2x pixels) against the real backend and saves PNGs to
    /// apps/mobile/TestResults/screens for review (ui-reviewer, DESIGN_SYSTEM). Opt-in: needs `make up` and
    /// GV_SCREENSHOTS=1 (`make unity-screens`), because the screens call the API.
    /// </summary>
    public class ScreenTourTests
    {
        private const int Width = 720;
        private const int Height = 1600;

        private AppServices services = null!;
        private RenderTexture target = null!;
        private GameObject host = null!;
        private string folder = string.Empty;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (Environment.GetEnvironmentVariable("GV_SCREENSHOTS") != "1")
            {
                Assert.Ignore("Set GV_SCREENSHOTS=1 and run `make up` to render the screen tour (make unity-screens).");
            }

            // A fresh first run: forget this editor's dev guest.
            foreach (var key in new[] { PlayerSession.GuestIdKey, PlayerSession.DeviceSecretKey, PlayerSession.RefreshTokenKey })
            {
                PlayerPrefs.DeleteKey("gv.dev." + key);
            }

            folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "screens"));
            Directory.CreateDirectory(folder);
            target = new RenderTexture(Width, Height, 24);
#if UNITY_EDITOR
            var shared = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/GameVerse/UI/GameVersePanelSettings.asset");
#else
            PanelSettings shared = null!;
            Assert.Ignore("The screen tour runs in the editor.");
#endif
            var panel = UnityEngine.Object.Instantiate(shared); // a copy: never retarget the project's panel asset
            panel.targetTexture = target;
            panel.clearColor = true;
            panel.colorClearValue = GameVerse.UI.GameVerseTokens.Color.Bg.Base;

            host = new GameObject("ScreenTour");
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = panel;
            yield return null;
            var root = document.rootVisualElement;
            root.AddToClassList("gv-root");
            root.styleSheets.Add(Resources.Load<StyleSheet>("Screens/Screens"));
            services = new AppServices(root);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (host != null)
            {
                UnityEngine.Object.Destroy(host);
                target.Release();
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Every_screen_renders_at_phone_size()
        {
            services.Router.Push(new WelcomeScreen(services));
            yield return Wait(1);
            Capture("01-welcome");

            var signIn = services.Session.SignInAsync(default).AsTask();
            yield return Until(() => signIn.IsCompleted, 15);
            Assert.That(services.Session.IsSignedIn, Is.True, signIn.Exception?.ToString());

            services.Router.ResetTo(new HomeScreen(services));
            yield return Wait(0.3f);
            Capture("02-home-loading");
            yield return Until(() => services.Router.Top!.Root.Q<GameCard>() != null, 20); // a cold API can take seconds
            yield return Wait(0.5f);
            Capture("03-home");

            var gamesTask = services.Api.GetAsync<List<GameInfo>>("/catalog/games").AsTask();
            yield return Until(() => gamesTask.IsCompleted, 10);
            var ludo = gamesTask.Result.Single(g => g.Id == "ludo");
            var lobby = new LobbyScreen(services, ludo);
            services.Router.Push(lobby);
            yield return Wait(1.5f);
            Capture("04-lobby");

            new HouseRules().OpenSheet(services.Overlay, true);
            yield return Wait(0.6f);
            Capture("05-house-rules");
            services.Overlay.Q<Sheet>()?.Close();

            var queue = services.Api.PostAsync<QueueStatusDto>("/matchmaking/queue", new JoinQueueRequest { GameId = "ludo", ModeKey = "ranked", PlayerCount = 2 }).AsTask();
            yield return Until(() => queue.IsCompleted, 10);
            services.Router.Push(new MatchmakingScreen(services, ludo, ludo.Modes.Single(m => m.Key == "ranked"), 2));
            yield return Wait(3);
            Capture("06-matchmaking");
            var cancel = services.Api.DeleteAsync("/matchmaking/queue").AsTask();
            yield return Until(() => cancel.IsCompleted, 10);
            services.Router.PopTo(lobby);

            var room = services.Api.PostAsync<RoomDto>("/rooms", new CreateRoomRequest { GameId = "ludo", PlayerCount = 4, BestOf = 3 }).AsTask();
            yield return Until(() => room.IsCompleted, 10);
            services.Router.Push(new RoomScreen(services, ludo, room.Result));
            yield return Wait(2.5f);
            Capture("07-room");
            services.Router.PopTo(lobby);

            var local = GameScreen.PassAndPlay(services, ludo, 4, LudoConfig.Default);
            services.Router.Push(local);
            yield return Wait(2);
            Capture("08-game-pass-and-play");
            for (var i = 0; i < 40; i++)
            {
                local.Module.AutoplayStep(); // a few moves in, so tokens are on the board
                yield return Wait(0.9f);
            }

            Capture("09-game-pass-the-phone");
            services.Router.PopTo(lobby);

            var vsAi = LobbyScreen.StartVsAiAsync(services, ludo, "vs-ai", 2, 2, new Dictionary<string, string>(), default).AsTask();
            yield return Until(() => vsAi.IsCompleted, 10);
            yield return Wait(3);
            Capture("10-game-vs-ai");
            var game = (GameScreen)services.Router.Top!;
            for (var i = 0; i < 30; i++)
            {
                game.Module.AutoplayStep();
                yield return Wait(0.8f);
            }

            Capture("10b-game-vs-ai-in-progress");
            game.Module.View.Q<GameVerse.Games.Ludo.View.LudoView>()!.Win(0);
            yield return Wait(0.45f);
            Capture("10c-win-confetti");
            services.Router.PopTo(lobby);

            var manifest = LocalLudoHost.CreateManifest(4);
            services.Router.Push(new ResultsScreen(services, ludo, manifest, new MatchEndedEvent { Kind = "win", WinningSeats = new List<int> { 2 }, Placements = new List<int> { 2, 0, 3, 1 }, Reason = "all_tokens_home" }, null));
            yield return Wait(1);
            Capture("11-results");
            Debug.Log("GV_SCREENS saved to " + folder);
        }

        private IEnumerator Wait(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                services.Tick();
                yield return null;
            }
        }

        private IEnumerator Until(Func<bool> done, float timeout)
        {
            var until = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < until)
            {
                services.Tick();
                yield return null;
            }

            Assert.That(done(), Is.True, "timed out");
        }

        private void Capture(string name)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }
    }
}
