#nullable enable
using UnityEngine;

namespace GameVerse.Platform.Shell
{
    /// <summary>
    /// Launch options for development builds only, used by the device acceptance check (ADR-0011):
    /// <c>adb shell am start -n app.gameverse.mobile/com.unity3d.player.UnityPlayerGameActivity -e gv_autoplay_room ABC234</c>
    /// makes the app sign in, join that room, ready up and play by itself through the normal screens. Release builds
    /// ignore every extra.
    /// </summary>
    public sealed class DevLaunch
    {
        private DevLaunch(string? autoplayRoom) => AutoplayRoom = autoplayRoom;

        /// <summary>A room code to join and autoplay, or null.</summary>
        public string? AutoplayRoom { get; }

        public bool Autoplay => AutoplayRoom != null;

        public static DevLaunch Read()
        {
            if (!Debug.isDebugBuild)
            {
                return new DevLaunch(null);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                var room = intent.Call<string>("getStringExtra", "gv_autoplay_room");
                return new DevLaunch(string.IsNullOrWhiteSpace(room) ? null : room.Trim().ToUpperInvariant());
            }
            catch (System.Exception)
            {
                return new DevLaunch(null);
            }
#else
            var fromEnvironment = System.Environment.GetEnvironmentVariable("GV_AUTOPLAY_ROOM");
            return new DevLaunch(string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim().ToUpperInvariant());
#endif
        }
    }
}
