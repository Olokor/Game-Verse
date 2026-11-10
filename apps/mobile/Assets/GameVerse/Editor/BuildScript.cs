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

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"GV_BUILD android result={summary.result} size={summary.totalSize} time={summary.totalTime} errors={summary.totalErrors} path={Path.GetFullPath(AndroidOutput)}");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            else if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Android build failed: " + summary.result);
            }
        }
    }
}
