#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Session;
using GameVerse.Net;
using GameVerse.Net.Http;
using GameVerse.Net.Realtime;
using GameVerse.Platform.Session;
using GameVerse.UI;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Platform.Shell
{
    /// <summary>What every screen can use: the signed-in session, the router, realtime connections, toasts.</summary>
    public sealed class AppServices
    {
        private readonly List<RealtimeConnection> live = new List<RealtimeConnection>();

        public AppServices(VisualElement root)
        {
            BaseUrl = AppConfig.ResolveApiBaseUrl();
            Session = new PlayerSession(BaseUrl, new UnityHttpTransport(), SecureStores.ForThisPlatform());
            var screens = new VisualElement { name = "screens" };
            screens.AddToClassList("gv-shell__screens");
            Overlay = new VisualElement { name = "overlay", pickingMode = PickingMode.Ignore };
            Overlay.AddToClassList("gv-shell__overlay");
            root.Add(screens);
            root.Add(Overlay);
            Router = new Router(screens, Overlay);
        }

        public string BaseUrl { get; }

        public PlayerSession Session { get; }

        public ApiClient Api => Session.Api;

        public Router Router { get; }

        /// <summary>Above every screen: toasts and sheets.</summary>
        public VisualElement Overlay { get; }

        /// <summary>Launch options for development builds (the autoplay acceptance check).</summary>
        public DevLaunch Dev { get; } = DevLaunch.Read();

        /// <summary>
        /// A realtime link ticked every frame until <see cref="Release"/>. New tickets come from POST /realtime/tickets.
        /// </summary>
        public RealtimeConnection Connect(RealtimeInfo first, object ticketRequest)
        {
            var connection = new RealtimeConnection(
                () => new NativeSocket(),
                ct => Api.PostAsync<RealtimeInfo>("/realtime/tickets", ticketRequest, ct),
                () => Time.realtimeSinceStartupAsDouble,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            live.Add(connection);
            connection.Start(first);
            return connection;
        }

        public void Release(RealtimeConnection connection)
        {
            live.Remove(connection);
            connection.Dispose();
        }

        public void Toast(string message, bool error = false) => UI.Toast.Show(Overlay, message, error);

        /// <summary>Shows an API failure as a toast, in the words the server used.</summary>
        public void Fail(Exception error) => Toast(error is ApiException api ? api.PlayerMessage : "Something went wrong. Try again.", true);

        public void Tick()
        {
            for (var i = live.Count - 1; i >= 0; i--)
            {
                live[i].Tick();
            }

            Router.Tick();
        }
    }

    /// <summary>
    /// One screen: a UXML layout from Resources/Screens plus the code that drives it. Screens are plain classes, not
    /// MonoBehaviours; the router calls <see cref="Tick"/> on the top screen every frame.
    /// </summary>
    public abstract class AppScreen
    {
        protected AppScreen(AppServices app, string layout)
        {
            App = app;
            Root = new VisualElement { name = layout };
            Root.AddToClassList("gv-root");
            Root.AddToClassList("gv-page");
            var tree = Resources.Load<VisualTreeAsset>("Screens/" + layout);
            if (tree == null)
            {
                throw new InvalidOperationException("Missing screen layout Resources/Screens/" + layout + ".uxml");
            }

            tree.CloneTree(Root);
        }

        public VisualElement Root { get; }

        protected AppServices App { get; }

        /// <summary>Cancelled when the screen leaves the stack.</summary>
        protected CancellationToken Lifetime => lifetime.Token;

        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        public virtual void OnEnter()
        {
        }

        /// <summary>The screen is on top again after the one above it closed.</summary>
        public virtual void OnResume()
        {
        }

        public virtual void OnExit()
        {
        }

        public virtual void Tick()
        {
        }

        /// <summary>Android back: return true when handled (closed a sheet, asked to confirm).</summary>
        public virtual bool OnBack() => false;

        internal void Dispose()
        {
            lifetime.Cancel();
            lifetime.Dispose();
            OnExit();
        }

        protected T Q<T>(string name)
            where T : VisualElement =>
            Root.Q<T>(name) ?? throw new InvalidOperationException(GetType().Name + " layout has no " + typeof(T).Name + " '" + name + "'");

        /// <summary>Runs an API call with the button in its loading state; failures become toasts.</summary>
        protected async UniTask<bool> Busy(Button button, Func<UniTask> work)
        {
            ButtonStyle.SetLoading(button, true);
            try
            {
                await work();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception error)
            {
                App.Fail(error);
                return false;
            }
            finally
            {
                ButtonStyle.SetLoading(button, false);
            }
        }
    }

    /// <summary>A stack of screens with the design system's 240ms transition (a fade under reduced motion).</summary>
    public sealed class Router
    {
        private readonly VisualElement container;
        private readonly VisualElement? overlay;
        private readonly List<AppScreen> stack = new List<AppScreen>();

        public Router(VisualElement container, VisualElement? overlay = null)
        {
            this.container = container;
            this.overlay = overlay;
        }

        public AppScreen? Top => stack.Count > 0 ? stack[stack.Count - 1] : null;

        public void Push(AppScreen screen)
        {
            CloseSheets();
            if (Top != null)
            {
                Top.Root.style.display = DisplayStyle.None;
            }

            stack.Add(screen);
            Show(screen);
            screen.OnEnter();
        }

        /// <summary>Swaps the top screen (matchmaking becomes the game, the game becomes results).</summary>
        public void Replace(AppScreen screen)
        {
            if (Top is { } old)
            {
                stack.RemoveAt(stack.Count - 1);
                old.Root.RemoveFromHierarchy();
                old.Dispose();
            }

            Push(screen);
        }

        /// <summary>Clears the stack and shows one screen (Home).</summary>
        public void ResetTo(AppScreen screen)
        {
            while (stack.Count > 0)
            {
                var old = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                old.Root.RemoveFromHierarchy();
                old.Dispose();
            }

            Push(screen);
        }

        public void Pop()
        {
            if (stack.Count <= 1)
            {
                return;
            }

            CloseSheets();
            var old = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            old.Root.RemoveFromHierarchy();
            old.Dispose();
            Show(Top!);
            Top!.OnResume();
        }

        /// <summary>Pops screens until <paramref name="screen"/> is on top (results back to the room).</summary>
        public void PopTo(AppScreen screen)
        {
            while (Top != null && Top != screen && stack.Count > 1)
            {
                Pop();
            }
        }

        public bool Contains(AppScreen screen) => stack.Contains(screen);

        public void Back()
        {
            // An open sheet takes the back button first.
            if (overlay?.Query<Sheet>().Last() is { } sheet)
            {
                sheet.Close();
                return;
            }

            if (Top is { } top && !top.OnBack())
            {
                Haptics.Light();
                Pop();
            }
        }

        public void Tick()
        {
            Top?.Tick();
        }

        private void Show(AppScreen screen)
        {
            if (screen.Root.parent != container)
            {
                container.Add(screen.Root);
            }

            screen.Root.style.display = DisplayStyle.Flex;
            screen.Root.style.translate = new Translate(0, 0);
            if (MotionPreferences.Reduced)
            {
                Tween.Fade(screen.Root).Forget(); // reduced motion: a fade instead of the slide
                return;
            }

            Tween.Run(GameVerseTokens.Motion.Base.Duration, Tween.Standard, t =>
            {
                screen.Root.style.opacity = t;
                screen.Root.style.translate = new Translate((1 - t) * 24, 0);
            }).Forget();
        }

        private void CloseSheets()
        {
            if (overlay == null)
            {
                return;
            }

            foreach (var sheet in overlay.Query<Sheet>().ToList())
            {
                sheet.Close();
            }
        }
    }
}
