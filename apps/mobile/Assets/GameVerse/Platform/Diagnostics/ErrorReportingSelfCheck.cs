using Sentry;
using UnityEngine;

namespace GameVerse.Platform.Diagnostics
{
    /// <summary>
    /// Development builds only (called by Boot): sends one info event so `make device-check` can prove the error pipe
    /// works on a real device under IL2CPP, and logs one parseable line:
    ///   GV_ERRORCHECK sent id=&lt;event id&gt; device=&lt;model&gt;   reporting on, event queued for GlitchTip
    ///   GV_ERRORCHECK disabled                         no DSN in this build (UNITY_ERROR_DSN was empty)
    /// </summary>
    public static class ErrorReportingSelfCheck
    {
        public const string LogPrefix = "GV_ERRORCHECK";

        public static string Run()
        {
            if (!Sentry.Unity.SentrySdk.IsEnabled)
            {
                return LogPrefix + " disabled";
            }

            var device = SystemInfo.deviceModel;
            var id = Sentry.Unity.SentrySdk.CaptureMessage(LogPrefix + " " + device, SentryLevel.Info);
            Sentry.Unity.SentrySdk.Flush(System.TimeSpan.FromSeconds(5));
            return LogPrefix + " sent id=" + id + " device=" + device;
        }
    }
}
