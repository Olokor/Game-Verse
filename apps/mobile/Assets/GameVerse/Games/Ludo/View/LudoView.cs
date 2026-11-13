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
    /// The Ludo table: the board with its turn light, the pieces, an effects layer, and a dock under the board holding the
    /// die (in the thumb zone) with the turn prompt; plus the "pass the phone" curtain for pass-and-play. It only presents:
    /// <see cref="LudoModule"/> decides what is allowed (through <see cref="LudoController"/>) and plays each transition
    /// here in order (GAME_BRIEFS section 1: the die tumbles, tokens hop square by square with a squash, captured tokens
    /// fly home).
    /// </summary>
    public sealed class LudoView : VisualElement
    {
        private readonly BoardElement board = new BoardElement();
        private readonly VisualElement pieces = new VisualElement();
        private readonly VisualElement sparks = new VisualElement();
        private readonly DiceElement dice = new DiceElement();
        private readonly VisualElement dock = new VisualElement();
        private readonly VisualElement pip = new VisualElement();
        private readonly Label prompt = new Label();
        private readonly Label hint = new Label("TAP TO ROLL");
        private readonly VisualElement curtain = new VisualElement();
        private readonly Label curtainLabel = new Label();
        private readonly VisualElement curtainEmblem = new VisualElement();
        private readonly Button curtainButton = new Button();
        private readonly Label banner = new Label();
        private readonly LudoSounds sounds;
        private readonly Effects effects;
        private readonly System.Random faces = new System.Random();
        private TokenElement[][] tokens = new TokenElement[0][];
        private int[] colors = new int[0];
        private UniTaskCompletionSource? curtainTap;
        private bool wobbling;

        public LudoView(LudoSounds sounds)
        {
            this.sounds = sounds;
            AddToClassList("ludo");
            var table = new VisualElement { name = "table" };
            table.AddToClassList("ludo__table");
            board.AddToClassList("ludo__board");
            pieces.AddToClassList("ludo__layer");
            pieces.pickingMode = PickingMode.Ignore;
            sparks.AddToClassList("ludo__layer");
            sparks.pickingMode = PickingMode.Ignore;
            board.Add(pieces);
            board.Add(sparks);
            table.Add(board);
            Add(table);
            effects = new Effects(sparks, board.ToPixels, () => board.CellSize);

            dock.AddToClassList("ludo__dock");
            var row = new VisualElement();
            row.AddToClassList("ludo__prompt-row");
            pip.AddToClassList("ludo__pip");
            prompt.AddToClassList("ludo__prompt");
            row.Add(pip);
            row.Add(prompt);
            dice.AddToClassList("ludo__dice");
            hint.AddToClassList("ludo__hint");
            dock.Add(row);
            dock.Add(dice);
            dock.Add(hint);
            Add(dock);

            // Pass-and-play curtain: the next seat's emblem, its colour's name, and one button to take the phone.
            curtain.AddToClassList("ludo__curtain");
            curtainEmblem.AddToClassList("ludo__curtain-emblem");
            curtainLabel.AddToClassList("ludo__curtain-label");
            var curtainHint = new Label("Hand the phone over, then tap when it's yours");
            curtainHint.AddToClassList("ludo__curtain-hint");
            ButtonStyle.Apply(curtainButton, ButtonVariant.Primary, ButtonSize.Lg);
            curtainButton.AddToClassList("ludo__curtain-button");
            curtainButton.clicked += () => curtainTap?.TrySetResult();
            curtain.Add(curtainEmblem);
            curtain.Add(curtainLabel);
            curtain.Add(curtainHint);
            curtain.Add(curtainButton);
            curtain.style.display = DisplayStyle.None;
            Add(curtain);

            banner.AddToClassList("ludo__banner");
            banner.pickingMode = PickingMode.Ignore;
            banner.style.display = DisplayStyle.None;
            board.Add(banner);

            dice.RegisterCallback<PointerDownEvent>(_ => dice.style.scale = new Scale(Vector3.one * GameVerseTokens.Motion.PressScale));
            dice.RegisterCallback<PointerLeaveEvent>(_ => dice.style.scale = new Scale(Vector3.one));
            dice.RegisterCallback<PointerCancelEvent>(_ => dice.style.scale = new Scale(Vector3.one));
            dice.RegisterCallback<PointerCaptureOutEvent>(_ => dice.style.scale = new Scale(Vector3.one));
            dice.RegisterCallback<PointerUpEvent>(_ =>
            {
                dice.style.scale = new Scale(Vector3.one);
                Haptics.Light();
                RollTapped?.Invoke();
            });

            // The die nudges itself now and then while it waits for a tap.
            schedule.Execute(() =>
            {
                if (dice.Active && !wobbling && !MotionPreferences.Reduced)
                {
                    WobbleAsync().Forget();
                }
            }).Every(3500);

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
            dice.Value = state.LastRoll == 0 ? 6 : state.LastRoll;
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
                if (transition.Roll == 6)
                {
                    dice.FlashSix();
                    Haptics.Light();
                }
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
                    Haptics.Medium();
                    var victim = tokens[captured.Seat][captured.Token];
                    effects.Burst(victim.Cell, LudoPalette.Of(colors[mover]));
                    await LaunchHomeAsync(victim, LudoBoardLayout.TokenCentre(colors[captured.Seat], LudoBoard.Base, captured.Token), ct);
                }

                if (transition.ReachedHome)
                {
                    sounds.Home();
                    effects.Ripple(LudoBoardLayout.Centre, LudoPalette.Of(colors[mover]));
                }
            }

            dice.Value = state.LastRoll == 0 ? 6 : state.LastRoll; // a full face before the first roll, not an empty box
            Snap(state);
        }

        /// <summary>
        /// What this device may do now (the die, the tappable tokens) and whose turn it is: the yard light, the dock's
        /// colour and the prompt follow <paramref name="seatToAct"/>.
        /// </summary>
        public void SetInput(bool canRoll, IReadOnlyList<(int Seat, int Token)> movable, int? seatToAct, string text)
        {
            dice.Active = canRoll;
            hint.style.display = canRoll ? DisplayStyle.Flex : DisplayStyle.None;
            var turnColor = seatToAct is { } s && s < colors.Length ? colors[s] : (int?)null;
            board.SetTurn(turnColor);
            var accent = turnColor is { } c ? LudoPalette.Of(c) : (Color)GameVerseTokens.Color.Text.Muted;
            dice.Accent = accent;
            dock.style.backgroundColor = Color.Lerp(GameVerseTokens.Color.Bg.Surface, accent, 0.07f); // a hint of the seat, not a hairline
            pip.Clear();
            pip.style.backgroundColor = accent;
            if (turnColor is { } shape)
            {
                pip.Add(new ShapeBadge(shape));
            }

            var choosing = movable.Count > 0;
            foreach (var seat in tokens)
            {
                foreach (var token in seat)
                {
                    token.Movable = false;
                    token.Dimmed = choosing && seatToAct == token.Seat;
                }
            }

            foreach (var (seat, token) in movable)
            {
                tokens[seat][token].Movable = true;
                tokens[seat][token].Dimmed = false;
                tokens[seat][token].BringToFront();
            }

            prompt.text = text;
            LayoutTokens();
        }

        /// <summary>Pass-and-play: hides the board until the next player taps, so nobody plays the wrong colour.</summary>
        public async UniTask PassToAsync(int color, CancellationToken ct)
        {
            curtainLabel.text = "Pass to " + LudoPalette.Names[color];
            curtainLabel.style.color = LudoPalette.Of(color);
            curtainEmblem.Clear();
            curtainEmblem.style.backgroundColor = LudoPalette.Of(color);
            curtainEmblem.Add(new ShapeBadge(color));
            curtainButton.text = "I'm " + LudoPalette.Names[color];
            curtain.style.display = DisplayStyle.Flex;
            Tween.Fade(curtain).Forget();
            curtainTap = new UniTaskCompletionSource();
            await curtainTap.Task.AttachExternalCancellation(ct);
            curtain.style.display = DisplayStyle.None;
            Haptics.Light();
        }

        /// <summary>
        /// The win moment: fanfare, a success haptic, a banner in the winner's colour and confetti in volt and that
        /// colour. Under reduced motion the banner simply fades in.
        /// </summary>
        public void Win(int winnerSeat)
        {
            sounds.Win();
            Haptics.Success();
            var color = colors[winnerSeat];
            banner.text = LudoPalette.Names[color] + " wins!";
            banner.style.color = LudoPalette.Of(color);
            banner.style.display = DisplayStyle.Flex;
            Tween.Fade(banner).Forget();
            effects.Confetti(LudoPalette.Of(color));
        }

        private async UniTask TumbleAsync(int value, int color, CancellationToken ct)
        {
            sounds.Rattle();
            dice.Accent = LudoPalette.Of(color);
            var lastFace = -1f;
            await Tween.Run(GameVerseTokens.Motion.Game.DiceRoll, Tween.EaseOut, t =>
            {
                if (t - lastFace > 0.1f && t < 0.85f)
                {
                    lastFace = t;
                    dice.Value = faces.Next(1, 7);
                }

                // Up, spinning, then down with a squash as it lands.
                var rise = t < 0.7f ? Mathf.Sin(t / 0.7f * Mathf.PI) : 0;
                var land = t >= 0.7f ? Mathf.Sin((t - 0.7f) / 0.3f * Mathf.PI) : 0;
                dice.style.translate = new Translate(0, -rise * 22);
                dice.style.rotate = new Rotate(new Angle((1 - t) * 540, AngleUnit.Degree));
                dice.style.scale = new Scale(new Vector3(1 + (rise * 0.12f) + (land * 0.1f), 1 + (rise * 0.12f) - (land * 0.12f), 1));
            }, ct);
            dice.style.translate = new Translate(0, 0);
            dice.style.rotate = new Rotate(new Angle(0, AngleUnit.Degree));
            dice.style.scale = new Scale(Vector3.one);
            dice.Value = value;
        }

        private async UniTaskVoid WobbleAsync()
        {
            wobbling = true;
            await Tween.Run(GameVerseTokens.Motion.Slow.Duration, t => t, t =>
                dice.style.rotate = new Rotate(new Angle(Mathf.Sin(t * Mathf.PI * 3) * 7 * (1 - t), AngleUnit.Degree)));
            wobbling = false;
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
                piece.Layout(board.CellSize, board.Origin, Vector2.zero);
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
                piece.Spin = t * 360;
                piece.Layout(board.CellSize, board.Origin, Vector2.zero);
            }, ct);
            piece.Squash = Vector2.one;
            piece.Spin = 0;
            piece.Cell = home;
        }

        private void Snap(LudoStateDto state)
        {
            for (var seat = 0; seat < tokens.Length; seat++)
            {
                for (var t = 0; t < LudoBoard.TokensPerSeat; t++)
                {
                    tokens[seat][t].Cell = LudoBoardLayout.TokenCentre(colors[seat], state.Tokens[seat][t], t);
                    tokens[seat][t].style.opacity = Array.IndexOf(state.Left, seat) >= 0 ? GameVerseTokens.Opacity.Inactive : 1f;
                }
            }

            LayoutTokens();
        }

        /// <summary>
        /// Places every token. Tokens sharing a square fan out slightly so each stays tappable, and a seat with several
        /// tokens on one square shows "×n" on its top token.
        /// </summary>
        private void LayoutTokens()
        {
            var cellSize = board.CellSize;
            if (cellSize <= 0)
            {
                return;
            }

            var stacks = new Dictionary<Vector2, int>();
            var perSeat = new Dictionary<(Vector2, int), int>();
            foreach (var seat in tokens)
            {
                foreach (var token in seat)
                {
                    stacks.TryGetValue(token.Cell, out var count);
                    stacks[token.Cell] = count + 1;
                    perSeat.TryGetValue((token.Cell, token.Seat), out var mine);
                    perSeat[(token.Cell, token.Seat)] = mine + 1;
                }
            }

            var placed = new Dictionary<Vector2, int>();
            var labelled = new HashSet<(Vector2, int)>();
            foreach (var seat in tokens)
            {
                foreach (var token in seat)
                {
                    placed.TryGetValue(token.Cell, out var index);
                    placed[token.Cell] = index + 1;
                    var count = stacks[token.Cell];
                    var offset = count > 1 ? new Vector2((index - ((count - 1) / 2f)) * 0.22f, 0) : Vector2.zero;
                    var inYard = IsInYard(token.Cell);
                    token.Layout(cellSize, board.Origin, offset, inYard);
                    var key = (token.Cell, token.Seat);
                    var onTrack = !inYard;
                    token.SetStack(onTrack && perSeat[key] > 1 && labelled.Add(key) ? perSeat[key] : 1);
                }
            }
        }

        private static bool IsInYard(Vector2 cell)
        {
            foreach (var origin in LudoBoardLayout.BaseOrigin)
            {
                if (cell.x >= origin.x && cell.x <= origin.x + 6 && cell.y >= origin.y && cell.y <= origin.y + 6)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
