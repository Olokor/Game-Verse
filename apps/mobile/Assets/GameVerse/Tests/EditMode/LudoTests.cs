#nullable enable
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Events;
using GameVerse.Contracts.Ludo;
using GameVerse.Contracts.Session;
using GameVerse.Core;
using GameVerse.Games.Ludo;
using GameVerse.Games.Ludo.Logic;
using GameVerse.Net;
using GameVerse.Rules.Engine;
using GameVerse.Rules.Ludo;
using NUnit.Framework;
using UnityEngine;

namespace GameVerse.Tests
{
    public class LudoBoardLayoutTests
    {
        [Test]
        public void The_track_is_52_distinct_squares_each_next_to_the_one_before()
        {
            var track = LudoBoardLayout.Track;
            Assert.That(track.Length, Is.EqualTo(52));
            Assert.That(track.Distinct().Count(), Is.EqualTo(52));
            var diagonals = 0;
            for (var i = 0; i < track.Length; i++)
            {
                var step = track[(i + 1) % track.Length] - track[i];
                Assert.That(Mathf.Abs(step.x) <= 1 && Mathf.Abs(step.y) <= 1 && step != Vector2Int.zero, Is.True, $"square {i} to {i + 1}");
                diagonals += Mathf.Abs(step.x) + Mathf.Abs(step.y) == 2 ? 1 : 0;
            }

            // As on a real board, the track cuts the four inner corners diagonally (e.g. (5,6) to (6,5)) to stay at 52 squares.
            Assert.That(diagonals, Is.EqualTo(4));
        }

        [Test]
        public void Each_colour_starts_beside_its_own_yard_on_a_safe_square()
        {
            for (var color = 0; color < 4; color++)
            {
                var start = LudoBoardLayout.Cell(color, 0);
                var yard = LudoBoardLayout.BaseOrigin[color];
                var nearestX = Mathf.Clamp(start.x, yard.x, yard.x + 5);
                var nearestY = Mathf.Clamp(start.y, yard.y, yard.y + 5);
                Assert.That(Mathf.Abs(start.x - nearestX) + Mathf.Abs(start.y - nearestY), Is.EqualTo(1), $"colour {color} start {start}");
                Assert.That(LudoBoard.IsSafe(LudoBoard.AbsoluteSquare(color, 0)), Is.True);
            }

            // Every safe square the engine knows is a track square the board draws.
            Assert.That(LudoBoard.SafeAbsoluteSquares.All(i => i >= 0 && i < LudoBoardLayout.Track.Length), Is.True);
        }

        [Test]
        public void A_path_leaves_the_track_into_its_own_home_column_and_ends_in_the_centre()
        {
            for (var color = 0; color < 4; color++)
            {
                var path = LudoBoardLayout.Path(color, 0, 48, LudoBoard.Home);
                Assert.That(path.Length, Is.EqualTo(8));
                for (var i = 1; i < path.Length - 1; i++)
                {
                    var step = path[i] - path[i - 1];
                    Assert.That(Mathf.Abs(step.x) + Mathf.Abs(step.y), Is.EqualTo(1).Within(0.001f), $"colour {color} hop {i}");
                }

                var lastColumnCell = LudoBoardLayout.Cell(color, LudoBoard.Home - 1);
                Assert.That(Mathf.Abs(lastColumnCell.x - 7) + Mathf.Abs(lastColumnCell.y - 7), Is.EqualTo(2), "the column ends next to the centre square");
                Assert.That(Vector2.Distance(path[path.Length - 1], LudoBoardLayout.Centre), Is.LessThan(1.6f));
            }
        }

        [Test]
        public void Leaving_the_yard_is_one_hop_onto_the_start_square()
        {
            var path = LudoBoardLayout.Path(2, 3, LudoBoard.Base, 0);
            Assert.That(path, Has.Length.EqualTo(1));
            Assert.That(path[0], Is.EqualTo(new Vector2(13.5f, 8.5f)));
        }
    }

    public class LudoLogicTests
    {
        private static readonly LudoEngine Engine = new LudoEngine();

        [Test]
        public void A_view_mapped_back_gives_the_engine_the_same_legal_moves()
        {
            var state = Engine.NewGame(new LudoConfig(quickMode: true), Seats(4), 99);
            for (var i = 0; i < 120 && Engine.Outcome(state) is null; i++)
            {
                var seat = state.CurrentSeat;
                var view = LudoStateMapper.ToState(Roundtrip(LudoStateMapper.ToDto(Engine.ViewFor(state, seat))));
                Assert.That(Engine.LegalActions(view, seat).Select(a => a.ToString()), Is.EqualTo(Engine.LegalActions(state, seat).Select(a => a.ToString())), $"step {i}");
                state = Engine.Apply(state, seat, Engine.LegalActions(state, seat).Last()).State;
            }
        }

        [Test]
        public void A_transition_tells_a_roll_from_a_move_and_finds_captures()
        {
            var before = Dto(current: 0, phase: "move", roll: 3, moveNumber: 7, tokens: new[] { new[] { 10, -1, -1, -1 }, new[] { -1, -1, -1, -1 } });
            before.Tokens[1][2] = 31; // an opponent token that is sent home by the move below
            var capture = Dto(current: 0, phase: "roll", roll: 3, moveNumber: 8, tokens: new[] { new[] { 13, -1, -1, -1 }, new[] { -1, -1, -1, -1 } });
            var rolled = Dto(current: 0, phase: "move", roll: 6, moveNumber: 9, tokens: capture.Tokens);

            var move = LudoTransition.Between(before, capture);
            var roll = LudoTransition.Between(capture, rolled);
            var jump = LudoTransition.Between(before, rolled);

            Assert.That((move.MoverSeat, move.From, move.To), Is.EqualTo(((int?)0, 10, 13)));
            Assert.That(move.Captures.Single().Seat, Is.EqualTo(1));
            Assert.That(move.RolledBy, Is.Null);
            Assert.That((roll.RolledBy, roll.Roll), Is.EqualTo(((int?)0, 6)));
            Assert.That(jump.Animate, Is.False, "two steps at once (a reconnect snapshot) are shown, not animated");
        }

        [Test]
        public void Input_is_offered_only_to_the_local_seat_and_only_once_until_the_answer()
        {
            var local = new LudoController(new[] { 0 });
            local.Apply(Dto(current: 1, phase: "roll", roll: 0, moveNumber: 0, tokens: new[] { new[] { -1, -1, -1, -1 }, new[] { -1, -1, -1, -1 } }));
            Assert.That(local.CanRoll, Is.False, "not this device's turn");
            Assert.That(local.Roll(), Is.Null);

            local.Apply(Dto(current: 0, phase: "roll", roll: 0, moveNumber: 1, tokens: new[] { new[] { -1, -1, -1, -1 }, new[] { -1, -1, -1, -1 } }));
            Assert.That(local.Roll(), Is.Not.Null);
            Assert.That(local.Roll(), Is.Null, "one action in flight");

            local.Apply(Dto(current: 0, phase: "move", roll: 6, moveNumber: 2, tokens: new[] { new[] { 4, -1, -1, -1 }, new[] { -1, -1, -1, -1 } }));
            Assert.That(local.MovableTokens(), Is.EquivalentTo(new[] { (0, 0), (0, 1), (0, 2), (0, 3) }), "a six: any token may move or leave the yard");
            Assert.That(local.Move(1, 0), Is.Null, "never another seat's token");
            Assert.That(local.Move(0, 2), Is.Not.Null);
        }

        [Test]
        public void A_whole_pass_and_play_game_runs_through_the_local_host()
        {
            var manifest = LocalLudoHost.CreateManifest(3);
            var host = new LocalLudoHost(manifest, new LudoConfig(quickMode: true), seed: 20261113);
            var player = new AutoPlayer(host);
            MatchEndedEvent? ended = null;
            host.MatchEnded += result => ended = result;

            host.Attach(player);
            for (var i = 0; i < 5000 && ended is null; i++)
            {
                player.Act();
            }

            Assert.That(ended, Is.Not.Null, "the game finished");
            Assert.That(ended!.Reason, Is.EqualTo("all_tokens_home"));
            Assert.That(player.Versions, Is.EqualTo(Enumerable.Range(0, player.Versions.Count).Select(v => (long)v)), "versions 0, 1, 2 ... with no gaps");
        }

        private static List<SeatInfo> Seats(int count) => Enumerable.Range(0, count).Select(s => new SeatInfo(s, "p" + s, false)).ToList();

        private static LudoStateDto Roundtrip(LudoStateDto dto) => GameVerseJson.Deserialize<LudoStateDto>(GameVerseJson.Serialize(dto));

        private static LudoStateDto Dto(int current, string phase, int roll, int moveNumber, int[][] tokens) => new LudoStateDto
        {
            Rules = new LudoRulesDto { SixToLeaveBase = true, ThreeSixesForfeit = true, CaptureBonus = true, SafeSquares = true, Placings = 1 },
            Colors = new[] { 0, 2 },
            Tokens = tokens.Select(t => t.ToArray()).ToArray(),
            CurrentSeat = current,
            Phase = phase,
            LastRoll = roll,
            MoveNumber = moveNumber,
        };

        /// <summary>A stand-in game module that plays every local seat through the real controller.</summary>
        private sealed class AutoPlayer : IGameModule
        {
            private readonly LocalLudoHost host;
            private readonly LudoController controller;
            private long seq;

            public AutoPlayer(LocalLudoHost host)
            {
                this.host = host;
                controller = new LudoController(host.Manifest.Players.Select(p => p.Seat).ToList());
            }

            public List<long> Versions { get; } = new List<long>();

            public string GameId => "ludo";

            public UniTask LoadAsync(SessionManifest manifest, IGameHost gameHost) => UniTask.CompletedTask;

            public void ApplyServerState(GameStateEnvelope envelope)
            {
                Versions.Add(envelope.StateVersion);
                controller.Apply(GameVerseJson.Deserialize<LudoStateDto>(envelope.StateJson));
            }

            public void Act()
            {
                var action = controller.Roll() ?? (controller.MovableTokens().FirstOrDefault() is var (seat, token) && controller.MovableTokens().Count > 0 ? controller.Move(seat, token) : null);
                if (action != null)
                {
                    host.SubmitAction(++seq, action);
                }
            }

            public UniTask UnloadAsync() => UniTask.CompletedTask;
        }
    }
}
