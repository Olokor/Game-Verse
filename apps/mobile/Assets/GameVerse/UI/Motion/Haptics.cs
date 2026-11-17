#nullable enable
using UnityEngine;

namespace GameVerse.UI.Motion
{
    /// <summary>
    /// Haptics (DESIGN_SYSTEM section 9): light on press, medium when a match is found, a pattern on a win. Android uses
    /// short vibration effects with an amplitude, which feel like taps, rather than Unity's long buzz. Each can be turned
    /// off in settings (<see cref="Enabled"/>).
    /// </summary>
    public static class Haptics
    {
        private const string PrefKey = "gv.haptics";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set => PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
        }

        public static void Light() => Pulse(12, 60);

        public static void Medium() => Pulse(25, 140);

        public static void Success()
        {
            Pulse(20, 120);
            Pulse(40, 200);
        }

        private static void Pulse(long milliseconds, int amplitude)
        {
            if (!Enabled)
            {
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                using var effect = new AndroidJavaClass("android.os.VibrationEffect").CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude);
                vibrator.Call("vibrate", effect);
            }
            catch (System.Exception)
            {
                // No vibrator, or the VIBRATE permission is missing: haptics are a nicety.
            }
#endif
        }

#if UNITY_ANDROID
        // Referencing Handheld.Vibrate makes Unity add the VIBRATE permission to the Android manifest. Never called.
        private static void ManifestHook() => Handheld.Vibrate();
#endif
    }
}
