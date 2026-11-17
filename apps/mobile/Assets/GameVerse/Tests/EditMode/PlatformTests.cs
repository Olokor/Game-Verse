#nullable enable
using System.Collections.Generic;
using GameVerse.Contracts.Ludo;
using GameVerse.Platform.Screens;
using GameVerse.Platform.Shell;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace GameVerse.Tests
{
    public class RouterTests
    {
        [Test]
        public void Push_pop_replace_and_reset_keep_one_visible_screen_and_dispose_the_rest()
        {
            var container = new VisualElement();
            var router = new Router(container);
            var home = new Probe("home");
            var lobby = new Probe("lobby");
            var game = new Probe("game");
            var results = new Probe("results");

            router.Push(home);
            router.Push(lobby);
            Assert.That(home.Root.style.display.value, Is.EqualTo(DisplayStyle.None), "only the top screen shows");

            router.Push(game);
            router.Replace(results); // the game becomes its results
            Assert.That(game.Exited, Is.True);
            Assert.That(router.Top, Is.SameAs(results));

            router.PopTo(lobby);
            Assert.That(router.Top, Is.SameAs(lobby));
            Assert.That(results.Exited, Is.True);
            Assert.That(lobby.Resumed, Is.EqualTo(1));

            router.Back(); // Android back pops when the screen does not handle it
            Assert.That(router.Top, Is.SameAs(home));
            router.Back();
            Assert.That(router.Top, Is.SameAs(home), "the last screen stays");

            var fresh = new Probe("fresh");
            router.ResetTo(fresh);
            Assert.That(home.Exited, Is.True);
            Assert.That(container.childCount, Is.EqualTo(1));
        }

        [Test]
        public void A_screen_can_keep_the_back_button_for_itself()
        {
            var router = new Router(new VisualElement());
            router.Push(new Probe("home"));
            var game = new Probe("game") { HandlesBack = true }; // the game asks to confirm leaving
            router.Push(game);

            router.Back();

            Assert.That(router.Top, Is.SameAs(game));
        }

        /// <summary>A screen that records what the router did to it. Uses the Welcome layout as any layout.</summary>
        private sealed class Probe : AppScreen
        {
            public Probe(string name)
                : base(new AppServicesStub().Services, "Welcome") => Root.name = name;

            public bool Exited { get; private set; }

            public int Resumed { get; private set; }

            public bool HandlesBack { get; set; }

            public override void OnExit() => Exited = true;

            public override void OnResume() => Resumed++;

            public override bool OnBack() => HandlesBack;
        }

        private sealed class AppServicesStub
        {
            public AppServices Services { get; } = new AppServices(new VisualElement());
        }
    }

    public class HouseRulesTests
    {
        [Test]
        public void The_defaults_are_Classic_Naija_and_teams_are_only_sent_for_four_players()
        {
            var rules = new HouseRules();

            var two = rules.ToWire(2);
            var four = rules.ToWire(4);

            Assert.That(two, Is.EquivalentTo(new Dictionary<string, string>
            {
                [LudoHouseRules.SixToLeaveBase] = "true",
                [LudoHouseRules.ThreeSixesForfeit] = "true",
                [LudoHouseRules.CaptureBonus] = "true",
                [LudoHouseRules.SafeSquares] = "true",
                [LudoHouseRules.MustCapture] = "false",
                [LudoHouseRules.QuickMode] = "false",
                [LudoHouseRules.Placings] = "1",
            }));
            Assert.That(four[LudoHouseRules.Teams], Is.EqualTo("false"));

            var config = rules.ToConfig(2);
            Assert.That(config.SixToLeaveBase && config.ThreeSixesForfeit && config.CaptureBonus && config.SafeSquares, Is.True);
            Assert.That(config.MustCapture || config.QuickMode || config.Teams, Is.False);
            Assert.That(config.Placings, Is.EqualTo(1));
        }
    }
}
