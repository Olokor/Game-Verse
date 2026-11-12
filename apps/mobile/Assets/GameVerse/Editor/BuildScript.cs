using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GameVerse.Editor
{
    /// <summary>
    /// Command-line builds. `make android` runs:
    ///   Unity -batchmode -quit -projectPath apps/mobile -buildTarget Android -executeMethod GameVerse.Editor.BuildScript.BuildAndroid
    /// Development IL2CPP ARM64 APK at apps/mobile/Build/Android/GameVerse.apk. Exits non-zero on failure.
    /// TODO(gameverse): release builds (AAB, signing keystore from a secret store) arrive with store submission.
    /// </summary>
    public static class BuildScript
    {
        public const string AndroidOutput = "Build/Android/GameVerse.apk";

        public static void BuildAndroid()
        {
            ProjectSetup.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(AndroidOutput));

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = AndroidOutput,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development,
            };

            BuildReport report;
            using (ErrorReportingDsn.ForBuild())
            {
                report = BuildPipeline.BuildPlayer(options);
            }

            var summary = report.summary;
            Debug.Log($"GV_BUILD android result={summary.result} size={summary.totalSize} time={summary.totalTime} errors={summary.totalErrors} path={Path.GetFullPath(AndroidOutput)}");

            Finish(summary.result == BuildResult.Succeeded, "Android build failed: " + summary.result);
        }

        private static void Finish(bool succeeded, string failure)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(succeeded ? 0 : 1);
            }
            else if (!succeeded)
            {
                throw new InvalidOperationException(failure);
            }
        }
    }

    /// <summary>
    /// Puts UNITY_ERROR_DSN (environment variable, set by `make` from .env) into the Sentry options asset for the
    /// duration of a build and clears it afterwards, so the committed asset never carries a DSN (ADR-0005). An empty
    /// variable builds with reporting off; the device self-check then logs "GV_ERRORCHECK disabled".
    /// </summary>
    internal sealed class ErrorReportingDsn : IDisposable
    {
        private ErrorReportingDsn()
        {
        }

        public static ErrorReportingDsn ForBuild()
        {
            var dsn = Environment.GetEnvironmentVariable("UNITY_ERROR_DSN") ?? string.Empty;
            var options = ProjectSetup.ConfigureErrorReporting();
            options.Dsn = dsn.Trim();
            options.Debug = false;
            EditorUtility.SetDirty(options);
            AssetDatabase.SaveAssets();
            Debug.Log("GV_BUILD error-reporting " + (options.Dsn.Length > 0 ? "on" : "off (UNITY_ERROR_DSN empty)"));
            return new ErrorReportingDsn();
        }

        public void Dispose()
        {
            var options = ProjectSetup.ConfigureErrorReporting(); // resets the DSN to empty
            EditorUtility.SetDirty(options);
            AssetDatabase.SaveAssets();
        }
    }
}
