#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Catalog;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Matchmaking;
using GameVerse.Contracts.Session;
using GameVerse.Platform.Shell;
using GameVerse.UI;
using UnityEngine.UIElements;

namespace GameVerse.Platform.Screens
{
    /// <summary>
    /// The game lobby (DESIGN_SYSTEM screen 4): mode tiles from the catalog (Quick match, Ranked, Private room, vs AI,
    /// Pass and play), the player count each mode allows, the AI level, best of 1 or 3 for rooms, the house rules sheet,
    /// and join-by-code. Play starts the chosen mode through the backend (or on the device for pass-and-play).
    /// </summary>
    public sealed class LobbyScreen : AppScreen
    {
        private readonly GameInfo game;
        private readonly Dictionary<string, ModeTile> tiles = new Dictionary<string, ModeTile>();
        private readonly HouseRules rules = new HouseRules();
        private readonly Button play;
        private GameModeInfo? mode;
        private int players = 2;
        private int aiLevel = 2;
        private int bestOf = 1;

        public LobbyScreen(AppServices app, GameInfo game)
            : base(app, "Lobby")
        {
            this.game = game;
            Q<Label>("title").text = game.Title;
            Q<Button>("back").clicked += App.Router.Back;
            play = Q<Button>("play");
            play.clicked += () => PlayAsync().Forget();
            Q<Button>("rules").clicked += () => rules.OpenSheet(App.Overlay, players == 4);
            Q<Button>("join").clicked += () => JoinAsync(Q<TextField>("code").value).Forget();

            var modes = Q<VisualElement>("modes");
            foreach (var m in game.Modes)
            {
                var tile = new ModeTile(m.Key, m.Label, Describe(m));
                tile.clicked += () => Select(m);
                tiles[m.Key] = tile;
                modes.Add(tile);
            }

            Choices.Fill(Q<VisualElement>("ai-levels"), new[] { (1, "Easy"), (2, "Medium"), (3, "Hard") }, aiLevel, v => aiLevel = v);
            Choices.Fill(Q<VisualElement>("best-of"), new[] { (1, "One game"), (3, "Best of three") }, bestOf, v => bestOf = v);
            Select(game.Modes.FirstOrDefault(m => m.Kind == ModeKinds.OnlineCasual) ?? game.Modes.First());
        }

        public override void OnEnter()
        {
            LoadRatingAsync().Forget();
            if (App.Dev.AutoplayRoom is { } code)
            {
                JoinAsync(code).Forget();
            }
        }

        private async UniTaskVoid LoadRatingAsync()
        {
            try
            {
                var rating = await App.Api.GetAsync<RatingDto>("/matchmaking/ratings/" + game.Id, Lifetime);
                Q<Label>("rating").text = "Your " + game.Title + " rating: " + rating.Rating + (rating.Provisional ? " (still settling)" : string.Empty);
            }
            catch (Exception)
            {
                Q<Label>("rating").text = string.Empty; // not worth an error state on its own
            }
        }

        private void Select(GameModeInfo selected)
        {
            mode = selected;
            foreach (var tile in tiles.Values)
            {
                tile.Selected = tile.Key == selected.Key;
            }

            if (!selected.PlayerCounts.Contains(players))
            {
                players = selected.PlayerCounts.First();
            }

            Choices.Fill(Q<VisualElement>("players"), selected.PlayerCounts.Select(n => (n, n + " players")).ToArray(), players, v => players = v);
            Show("ai", selected.Kind == ModeKinds.VsAi);
            Show("series", selected.Kind == ModeKinds.OnlinePrivate);
            Show("rules", selected.Kind is ModeKinds.OnlinePrivate or ModeKinds.VsAi or ModeKinds.Local);
            play.text = selected.Kind == ModeKinds.OnlinePrivate ? "Create room" : "Play";
        }

        private async UniTaskVoid PlayAsync()
        {
            if (mode is null)
            {
                return;
            }

            var chosen = mode;
            await Busy(play, async () =>
            {
                switch (chosen.Kind)
                {
                    case ModeKinds.OnlineCasual:
                    case ModeKinds.OnlineRanked:
                        await App.Api.PostAsync<QueueStatusDto>("/matchmaking/queue", new JoinQueueRequest { GameId = game.Id, ModeKey = chosen.Key, PlayerCount = players }, Lifetime);
                        App.Router.Push(new MatchmakingScreen(App, game, chosen, players));
                        break;
                    case ModeKinds.OnlinePrivate:
                        var room = await App.Api.PostAsync<RoomDto>("/rooms", new CreateRoomRequest { GameId = game.Id, PlayerCount = players, HouseRules = rules.ToWire(players), BestOf = bestOf }, Lifetime);
                        App.Router.Push(new RoomScreen(App, game, room));
                        break;
                    case ModeKinds.VsAi:
                        await StartVsAiAsync(App, game, chosen.Key, players, aiLevel, rules.ToWire(players), Lifetime);
                        break;
                    case ModeKinds.Local:
                        App.Router.Push(GameScreen.PassAndPlay(App, game, players, rules.ToConfig(players)));
                        break;
                }
            });
        }

        /// <summary>Creates a vs AI session (played by the server's match host) and opens it.</summary>
        public static async UniTask StartVsAiAsync(AppServices app, GameInfo game, string modeKey, int players, int level, Dictionary<string, string> houseRules, System.Threading.CancellationToken ct)
        {
            var manifest = await CreateVsAiAsync(app, game, modeKey, players, level, houseRules, ct);
            app.Router.Push(GameScreen.Online(app, game, manifest, null));
        }

        /// <summary>Creates a vs AI session and returns its manifest (with the socket ticket).</summary>
        public static UniTask<SessionManifest> CreateVsAiAsync(AppServices app, GameInfo game, string modeKey, int players, int level, Dictionary<string, string> houseRules, System.Threading.CancellationToken ct) =>
            app.Api.PostAsync<SessionManifest>("/sessions", new CreateSessionRequest
            {
                GameId = game.Id,
                Mode = modeKey,
                PlayerCount = players,
                AiLevel = level,
                HouseRules = houseRules,
            }, ct);

        private async UniTaskVoid JoinAsync(string code)
        {
            var join = Q<Button>("join");
            await Busy(join, async () =>
            {
                var room = await App.Api.PostAsync<RoomDto>("/rooms/" + Uri.EscapeDataString(code.Trim()) + "/join", null, Lifetime);
                App.Router.Push(new RoomScreen(App, game, room));
            });
        }

        private void Show(string name, bool visible) => Q<VisualElement>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private static string Describe(GameModeInfo mode) => mode.Kind switch
        {
            ModeKinds.OnlineCasual => "Play people online. If nobody is around, AI fills in.",
            ModeKinds.OnlineRanked => "Play people online for rating.",
            ModeKinds.OnlinePrivate => "Make a room and share the code with friends.",
            ModeKinds.VsAi => "Practise against the computer.",
            ModeKinds.Local => "Pass one phone around. Works offline.",
            _ => string.Empty,
        };
    }

    /// <summary>Pill choices (player count, AI level, best of): one is on at a time.</summary>
    public static class Choices
    {
        public static void Fill(VisualElement container, IReadOnlyList<(int Value, string Label)> options, int selected, Action<int> chosen)
        {
            container.Clear();
            container.AddToClassList("gv-choices");
            var buttons = new List<(int, Button)>();
            foreach (var (value, label) in options)
            {
                var button = new Button { text = label };
                button.AddToClassList("gv-choice");
                button.EnableInClassList("gv-choice--on", value == selected);
                button.clicked += () =>
                {
                    UI.Motion.Haptics.Light();
                    foreach (var (v, b) in buttons)
                    {
                        b.EnableInClassList("gv-choice--on", v == value);
                    }

                    chosen(value);
                };
                buttons.Add((value, button));
                container.Add(button);
            }
        }
    }

    /// <summary>The Ludo house rules a player chooses (keys in GameVerse.Contracts.Ludo.LudoHouseRules), defaulting to Classic Naija.</summary>
    public sealed class HouseRules
    {
        private static readonly (string Key, string Label)[] Toggles =
        {
            (LudoHouseRules.SixToLeaveBase, "Six to leave base"),
            (LudoHouseRules.ThreeSixesForfeit, "Three sixes lose the turn"),
            (LudoHouseRules.CaptureBonus, "Capture gives another roll"),
            (LudoHouseRules.SafeSquares, "Safe squares"),
            (LudoHouseRules.MustCapture, "Must capture when possible"),
            (LudoHouseRules.QuickMode, "Quick start (one token out)"),
            (LudoHouseRules.Teams, "Teams 2 v 2 (4 players)"),
        };

        private readonly Dictionary<string, bool> flags = new Dictionary<string, bool>
        {
            [LudoHouseRules.SixToLeaveBase] = true,
            [LudoHouseRules.ThreeSixesForfeit] = true,
            [LudoHouseRules.CaptureBonus] = true,
            [LudoHouseRules.SafeSquares] = true,
            [LudoHouseRules.MustCapture] = false,
            [LudoHouseRules.QuickMode] = false,
            [LudoHouseRules.Teams] = false,
        };

        private int placings = 1;

        public Sheet OpenSheet(VisualElement overlay, bool fourPlayers)
        {
            var sheet = new Sheet("House rules");
            var toggles = new VisualElement();
            toggles.AddToClassList("gv-choices");
            foreach (var (key, label) in Toggles)
            {
                if (key == LudoHouseRules.Teams && !fourPlayers)
                {
                    continue;
                }

                var button = new Button { text = label };
                button.AddToClassList("gv-choice");
                button.EnableInClassList("gv-choice--on", flags[key]);
                button.clicked += () =>
                {
                    flags[key] = !flags[key];
                    button.EnableInClassList("gv-choice--on", flags[key]);
                };
                toggles.Add(button);
            }

            var placingsLabel = new Label("GAME ENDS WHEN");
            placingsLabel.AddToClassList("gv-label");
            var placingChoices = new VisualElement();
            Choices.Fill(placingChoices, new[] { (1, "1st is home"), (2, "1st and 2nd"), (3, "Top three") }, placings, v => placings = v);
            var done = new Button { text = "Done" };
            ButtonStyle.Apply(done, ButtonVariant.Primary, ButtonSize.Md);
            done.clicked += sheet.Close;
            sheet.Content.Add(toggles);
            sheet.Content.Add(placingsLabel);
            sheet.Content.Add(placingChoices);
            sheet.Content.Add(done);
            sheet.Open(overlay);
            return sheet;
        }

        /// <summary>The rules as the server reads them; teams only with 4 players, placings capped by the engine.</summary>
        public Dictionary<string, string> ToWire(int playerCount)
        {
            var wire = new Dictionary<string, string>();
            foreach (var pair in flags)
            {
                if (pair.Key == LudoHouseRules.Teams && playerCount != 4)
                {
                    continue;
                }

                wire[pair.Key] = pair.Value ? "true" : "false";
            }

            wire[LudoHouseRules.Placings] = placings.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return wire;
        }

        public GameVerse.Rules.Ludo.LudoConfig ToConfig(int playerCount) => new GameVerse.Rules.Ludo.LudoConfig(
            sixToLeaveBase: flags[LudoHouseRules.SixToLeaveBase],
            threeSixesForfeit: flags[LudoHouseRules.ThreeSixesForfeit],
            captureBonus: flags[LudoHouseRules.CaptureBonus],
            mustCapture: flags[LudoHouseRules.MustCapture],
            safeSquares: flags[LudoHouseRules.SafeSquares],
            quickMode: flags[LudoHouseRules.QuickMode],
            placings: placings,
            teams: flags[LudoHouseRules.Teams] && playerCount == 4);
    }
}
