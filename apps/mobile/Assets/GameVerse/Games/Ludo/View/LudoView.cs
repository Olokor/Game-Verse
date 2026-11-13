#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameVerse.Contracts.Ludo;
using GameVerse.Games.Ludo.Logic;
using GameVerse.Rules.Ludo;
using GameVerse.UI;
using GameVerse.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// The Ludo table: board, tokens, the die and a one-line prompt, plus the "pass the phone" curtain for pass-and-play.
    /// It only presents: <see cref="LudoModule"/> decides what is allowed (through <see cref="LudoController"/>) and plays
    /// each transition here in order (GAME_BRIEFS section 1: the die tumbles, tokens hop square by square with a squash,
    /// captured tokens fly home).
    /// </summary>
    public sealed class LudoView : VisualElement
    {
        private readonly BoardElement board = new BoardElement();
        private readonly VisualElement pieces = new VisualElement();
        private readonly DiceElement dice = new DiceElement();
        private readonly Label prompt = new Label();
        private readonly VisualElement curtain = new VisualElement();
        private readonly Label curtainLabel = new Label();
        private readonly LudoSounds sounds;
        private readonly System.Random faces = new System.Random();
        private TokenElement[][] tokens = new TokenElement[0][];
        private int[] colors = new int[0];
        private UniTaskCompletionSource? curtainTap;

        public LudoView(LudoSounds sounds)
        {
            this.sounds = sounds;
            AddToClassList("ludo");
            var table = new VisualElement { name = "table" };
            table.AddToClassList("ludo__table");
            board.AddToClassList("ludo__board");
            pieces.AddToClassList("ludo__pieces");
            pieces.pickingMode = PickingMode.Ignore;
            board.Add(pieces);
            table.Add(board);
            Add(table);

            var bar = new VisualElement();
            bar.AddToClassList("ludo__bar");
            dice.AddToClassList("ludo__dice");
            prompt.AddToClassList("ludo__prompt");
            bar.Add(dice);
            bar.Add(prompt);
            Add(bar);

            curtain.AddToClassList("ludo__curtain");
            curtainLabel.AddToClassList("ludo__curtain-label");
            var hint = new Label("Tap when you have the phone");
            hint.AddToClassList("ludo__curtain-hint");
            curtain.Add(curtainLabel);
            curtain.Add(hint);
            curtain.style.display = DisplayStyle.None;
            curtain.RegisterCallback<PointerUpEvent>(_ => curtainTap?.TrySetResult());
            Add(curtain);

            dice.RegisterCallback<PointerUpEvent>(_ =>
            {
                Haptics.Light();
                RollTapped?.Invoke();
            });

            // The board is square: its height follows its width.
            board.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (Mathf.Abs(board.resolvedStyle.height - board.resolvedStyle.width) > 0.5f)
                {
                    board.style.height = board.resolvedStyle.width;
                }

                LayoutTokens();
            });
        }

        public event Action? RollTapped;

        public event Action<int, int>? TokenTapped;

        /// <summary>Builds the pieces for a game (once, from the first state).</summary>
        public void Setup(LudoStateDto state)
        {
            colors = state.Colors;
            board.Configure(colors, state.Rules.SafeSquares);
            pieces.Clear();
            tokens = new TokenElement[state.Tokens.Length][];
            for (var seat = 0; seat < tokens.Length; seat++)
            {
                tokens[seat] = new TokenElement[LudoBoard.TokensPerSeat];
                for (var t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    var element = new TokenElement(seat, t, colors[seat]);
                    var (s, k) = (seat, t);
                    element.RegisterCallback<PointerUpEvent>(_ =>
                    {
                        Haptics.Light();
                        TokenTapped?.Invoke(s, k);
                    });
                    tokens[seat][t] = element;
                    pieces.Add(element);
                }
            }

            Snap(state);
        }

        /// <summary>Plays one step (roll, move, captures) and ends with every piece exactly where the state says.</summary>
        public async UniTask PresentAsync(LudoStateDto state, LudoTransition transition, CancellationToken ct)
        {
            if (transition.Animate && transition.RolledBy is { } roller)
            {
                await TumbleAsync(transition.Roll, colors[roller], ct);
            }
            else if (transition.Animate && transition.MoverSeat is { } mover)
            {
                var piece = tokens[mover][transition.MoverToken];
                var path = LudoBoardLayout.Path(colors[mover], transition.MoverToken, transition.From, transition.To);
                for (var i = 0; i < path.Length; i++)
                {
                    await HopAsync(piece, path[i], ct);
                    sounds.Hop(i);
                }

                foreach (var captured in transition.Captures)
                {
                    sounds.Capture();
                    await LaunchHomeAsync(tokens[captured.Seat][captured.Token], LudoBoardLayout.TokenCentre(colors[captured.Seat], LudoBoard.Base, captured.Token), ct);
                }

                if (transition.ReachedHome)
                {
                    sounds.Home();
                }
            }

            dice.Value = state.LastRoll;
            Snap(state);
        }

        /// <summary>What this device may do now: the die, the tappable tokens, and the prompt line.</summary>
        public void SetInput(bool canRoll, IReadOnlyList<(int Seat, int Token)> movable, int? seatToAct, string text)
        {
            dice.Active = canRoll;
            dice.Accent = seatToAct is { } s ? LudoPalette.Of(colors[s]) : GameVerseTokens.Color.Text.Muted;
            foreach (var seat in tokens)
            {
                foreach (var token in seat)
                {
                    token.Movable = false;
                }
            }

            foreach (var (seat, token) in movable)
            {
                tokens[seat][token].Movable = true;
            }

            prompt.text = text;
        }

        /// <summary>Pass-and-play: hides the board until the next player taps, so nobody plays the wrong colour.</summary>
        public async UniTask PassToAsync(int color, CancellationToken ct)
        {
            curtainLabel.text = "Pass to " + LudoPalette.Names[color];
            curtainLabel.style.color = LudoPalette.Of(color);
            curtain.style.display = DisplayStyle.Flex;
            curtainTap = new UniTaskCompletionSource();
            await curtainTap.Task.AttachExternalCancellation(ct);
            curtain.style.display = DisplayStyle.None;
            Haptics.Light();
        }

        public void Win() => sounds.Win();

        private async UniTask TumbleAsync(int value, int color, CancellationToken ct)
        {
            sounds.Rattle();
            dice.Accent = LudoPalette.Of(color);
            var lastFace = -1f;
            await Tween.Run(GameVerseTokens.Motion.Game.DiceRoll, Tween.EaseOut, t =>
            {
                if (t - lastFace > 0.12f && t < 1)
                {
                    lastFace = t;
                    dice.Value = faces.Next(1, 7);
                }

                dice.style.rotate = new Rotate(new Angle((1 - t) * 540, AngleUnit.Degree));
                dice.style.scale = new Scale(Vector3.one * (1 + (Mathf.Sin(t * Mathf.PI) * 0.18f)));
            }, ct);
            dice.style.rotate = new Rotate(new Angle(0, AngleUnit.Degree));
            dice.style.scale = new Scale(Vector3.one);
            dice.Value = value;
        }

        private async UniTask HopAsync(TokenElement piece, Vector2 to, CancellationToken ct)
        {
            var from = piece.Cell;
            piece.BringToFront();
            await Tween.Run(GameVerseTokens.Motion.Game.TokenHop, Tween.Standard, t =>
            {
                // A small arc between squares, then a squash as it lands.
                piece.Cell = Vector2.Lerp(from, to, t) + new Vector2(0, -Mathf.Sin(t * Mathf.PI) * 0.35f);
                var squash = t > 0.8f ? 1 - (Mathf.Sin((t - 0.8f) / 0.2f * Mathf.PI) * 0.14f) : 1;
                piece.Squash = new Vector2(2 - squash, squash);
                piece.Layout(board.CellSize, Vector2.zero);
            }, ct);
            piece.Squash = Vector2.one;
            piece.Cell = to;
        }

        private async UniTask LaunchHomeAsync(TokenElement piece, Vector2 home, CancellationToken ct)
        {
            var from = piece.Cell;
            piece.BringToFront();
            await Tween.Run(GameVerseTokens.Motion.Game.Capture, Tween.EaseIn, t =>
            {
                piece.Cell = Vector2.Lerp(from, home, t) + new Vector2(0, -Mathf.Sin(t * Mathf.PI) * 2.5f);
                piece.Squash = Vector2.one * (1 + (Mathf.Sin(t * Mathf.PI) * 0.35f));
                piece.Layout(board.CellSize, Vector2.zero);
            }, ct);
            piece.Squash = Vector2.one;
            piece.Cell = home;
        }

        private void Snap(LudoStateDto state)
        {
            for (var seat = 0; seat < tokens.Length; seat++)
            {
                for (var t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    tokens[seat][t].Cell = LudoBoardLayout.TokenCentre(colors[seat], state.Tokens[seat][t], t);
                    tokens[seat][t].style.opacity = Array.IndexOf(state.Left, seat) >= 0 ? 0.35f : 1f;
                }
            }

            LayoutTokens();
        }

        /// <summary>Places every token; tokens sharing a square fan out slightly so each stays tappable.</summary>
        private void LayoutTokens()
        {
            var cellSize = board.CellSize;
            if (cellSize <= 0)
            {
                return;
            }

            var stacks = new Dictionary<Vector2, int>();
            foreach (var seat in tokens)
            {
                foreach (var token in seat)
                {
                    stacks.TryGetValue(token.Cell, out var count);
                    stacks[token.Cell] = count + 1;
                }
            }

            var placed = new Dictionary<Vector2, int>();
            foreach (var seat in tokens)
            {
                foreach (var token in seat)
                {
                    placed.TryGetValue(token.Cell, out var index);
                    placed[token.Cell] = index + 1;
                    var count = stacks[token.Cell];
                    var offset = count > 1 ? new Vector2((index - ((count - 1) / 2f)) * 0.22f, 0) : Vector2.zero;
                    token.Layout(cellSize, offset);
                }
            }
        }
    }
}
