#nullable enable
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Catalog;
using GameVerse.Platform.Shell;
using GameVerse.UI;
using UnityEngine.UIElements;

namespace GameVerse.Platform.Screens
{
    /// <summary>
    /// First run: one tap makes a guest account (DESIGN_SYSTEM screen 1). A returning player is signed in straight away
    /// behind a skeleton; if that fails (offline), the same screen offers Try again.
    /// </summary>
    public sealed class WelcomeScreen : AppScreen
    {
        private readonly Button play;
        private readonly Label error;
        private readonly VisualElement signingIn;
        private readonly VisualElement actions;

        public WelcomeScreen(AppServices app)
            : base(app, "Welcome")
        {
            play = Q<Button>("play");
            error = Q<Label>("error");
            signingIn = Q<VisualElement>("signing-in");
            actions = Q<VisualElement>("actions");
            signingIn.Add(new Skeleton(56));
            play.clicked += () => SignInAsync().Forget();
        }

        public override void OnEnter()
        {
            var returning = App.Session.HasAccount;
            play.text = returning ? "Try again" : "Play as guest";
            ShowSigningIn(returning);
            error.text = string.Empty;
            if (returning || App.Dev.Autoplay)
            {
                SignInAsync().Forget();
            }
        }

        private async UniTaskVoid SignInAsync()
        {
            error.text = string.Empty;
            var ok = await Busy(play, async () => await App.Session.SignInAsync(Lifetime));
            if (ok)
            {
                App.Router.ResetTo(new HomeScreen(App));
                return;
            }

            ShowSigningIn(false);
            play.text = App.Session.HasAccount ? "Try again" : "Play as guest";
            error.text = "We couldn't sign you in. Check your connection and try again.";
        }

        private void ShowSigningIn(bool busy)
        {
            signingIn.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
            actions.style.display = busy ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    /// <summary>
    /// Home (DESIGN_SYSTEM screen 2) on the real catalog: the first playable game is the hero, the others are inert
    /// "Coming soon" cards, as on web. Skeletons while loading; an error state with Try again.
    /// </summary>
    public sealed class HomeScreen : AppScreen
    {
        private readonly VisualElement featured;
        private readonly VisualElement more;
        private readonly VisualElement errorState;

        public HomeScreen(AppServices app)
            : base(app, "Home")
        {
            featured = Q<VisualElement>("featured");
            more = Q<VisualElement>("more");
            errorState = Q<VisualElement>("error-state");
            Q<Button>("retry").clicked += () => LoadAsync().Forget();
        }

        public override void OnEnter() => LoadAsync().Forget();

        private async UniTaskVoid LoadAsync()
        {
            errorState.style.display = DisplayStyle.None;
            featured.Clear();
            more.Clear();
            featured.Add(new Skeleton(240));
            more.Add(new Skeleton(88));
            more.Add(new Skeleton(88));

            List<GameInfo> games;
            try
            {
                games = await App.Api.GetAsync<List<GameInfo>>("/catalog/games", Lifetime);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }
            catch (System.Exception ex)
            {
                featured.Clear();
                more.Clear();
                errorState.style.display = DisplayStyle.Flex;
                Q<Label>("error").text = ex is Net.Http.ApiException api ? api.PlayerMessage : "Something went wrong.";
                return;
            }

            featured.Clear();
            more.Clear();
            var playable = games.Where(g => g.Status == GameStatuses.Enabled).ToList();
            foreach (var game in playable)
            {
                var card = new GameCard { Variant = playable.IndexOf(game) == 0 ? "hero" : "standard", Title = game.Title, Subtitle = game.Tagline, Category = game.Category };
                // One handler: a tap on the Play button bubbles up to the card, so the lobby opens once.
                card.RegisterCallback<ClickEvent>(_ => App.Router.Push(new LobbyScreen(App, game)));
                (playable.IndexOf(game) == 0 ? featured : more).Add(card);
            }

            var soon = games.Where(g => g.Status == GameStatuses.ComingSoon).ToList();
            if (playable.Count == 0 && soon.Count == 0)
            {
                var empty = new VisualElement();
                empty.AddToClassList("gv-empty");
                var title = new Label("No games right now");
                title.AddToClassList("gv-empty__title");
                var note = new Label("Games are being updated. Check back in a few minutes.");
                note.AddToClassList("gv-muted");
                empty.Add(title);
                empty.Add(note);
                featured.Add(empty);
                return;
            }

            if (soon.Count > 0)
            {
                var title = new Label("More games coming");
                title.AddToClassList("gv-section-title");
                more.Add(title);
                foreach (var game in soon)
                {
                    var card = new GameCard { Variant = "compact", Title = game.Title, Subtitle = "Coming soon", Category = game.Category };
                    card.pickingMode = PickingMode.Ignore; // not a link (ADR-0007)
                    card.style.opacity = GameVerseTokens.Opacity.Muted;
                    more.Add(card);
                }
            }

            if (App.Dev.Autoplay && playable.FirstOrDefault(g => g.Id == "ludo") is { } ludo)
            {
                App.Router.Push(new LobbyScreen(App, ludo)); // the acceptance check takes the normal path from Home
            }
        }
    }
}
