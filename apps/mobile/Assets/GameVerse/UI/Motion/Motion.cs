#nullable enable
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameVerse.UI.Motion
{
    /// <summary>
    /// Code tweens for UI Toolkit (DESIGN_SYSTEM section 5). A tween is a progress value 0..1 driven once per frame, which
    /// the caller maps onto styles. Durations and easings come from <see cref="GameVerseTokens.Motion"/>.
    /// Under reduced motion every tween jumps straight to its end (callers then fade instead of moving).
    /// </summary>
    public static class Tween
    {
        /// <summary>The design system's standard curve, cubic-bezier(0.2, 0.8, 0.2, 1) (motion.fast/base).</summary>
        public static float Standard(float t) => CubicBezier(0.2f, 0.8f, 0.2f, 1f, t);

        public static float EaseOut(float t) => 1 - ((1 - t) * (1 - t));

        public static float EaseIn(float t) => t * t;

        /// <summary>Runs <paramref name="apply"/> with eased progress every frame for <paramref name="seconds"/>.</summary>
        public static async UniTask Run(float seconds, Func<float, float> ease, Action<float> apply, CancellationToken ct = default)
        {
            if (seconds <= 0 || MotionPreferences.Reduced)
            {
                apply(1);
                return;
            }

            var start = Time.realtimeSinceStartup;
            while (true)
            {
                var t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / seconds);
                apply(ease(t));
                if (t >= 1)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        /// <summary>
        /// Fades an element in over motion.fast. Runs under reduced motion too: it is what replaces slides and scales there
        /// (DESIGN_SYSTEM section 5).
        /// </summary>
        public static async UniTask Fade(UnityEngine.UIElements.VisualElement element, CancellationToken ct = default)
        {
            var start = Time.realtimeSinceStartup;
            var seconds = GameVerseTokens.Motion.Fast.Duration;
            while (true)
            {
                var t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / seconds);
                element.style.opacity = t;
                if (t >= 1)
                {
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        /// <summary>CSS cubic-bezier(x1, y1, x2, y2) evaluated at time t (Newton iterations on x).</summary>
        public static float CubicBezier(float x1, float y1, float x2, float y2, float t)
        {
            float Sample(float a, float b, float u) => (3 * a * u * (1 - u) * (1 - u)) + (3 * b * u * u * (1 - u)) + (u * u * u);
            var u = t;
            for (var i = 0; i < 6; i++)
            {
                var x = Sample(x1, x2, u) - t;
                var dx = (3 * x1 * (1 - u) * (1 - u)) + (6 * (x2 - x1) * u * (1 - u)) + (3 * (1 - x2) * u * u);
                if (Mathf.Abs(dx) < 1e-5f)
                {
                    break;
                }

                u = Mathf.Clamp01(u - (x / dx));
            }

            return Sample(y1, y2, u);
        }
    }

    /// <summary>
    /// Reduced motion (DESIGN_SYSTEM section 5): on Android, the system "Remove animations" setting (animator duration scale
    /// 0). Settings can also turn it on in the app later.
    /// </summary>
    public static class MotionPreferences
    {
        private static bool? cached;

        /// <summary>Gets or sets a value indicating whether to replace motion with instant changes and fades.</summary>
        public static bool Reduced
        {
            get => cached ??= ReadSystemSetting();
            set => cached = value;
        }

        private static bool ReadSystemSetting()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var global = new AndroidJavaClass("android.provider.Settings$Global");
                return global.CallStatic<float>("getFloat", resolver, "animator_duration_scale", 1f) == 0f;
            }
            catch (Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
}
