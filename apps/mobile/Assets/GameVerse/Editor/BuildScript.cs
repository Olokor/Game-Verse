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

            Finish(summary.result == BuildResult.Succeeded, "Android build failed: " + summary.result);
        }

        /// <summary>
        /// Prompt 0.7 spike (throwaway, deleted with ADR-0002): one release Web build per scene, as each game ships its
        /// own small build, written to apps/web/public/unity/spike-2d and spike-3d. `make web-spike` runs:
        ///   Unity -batchmode -quit -projectPath apps/mobile -buildTarget WebGL -executeMethod GameVerse.Editor.BuildScript.BuildWebSpike
        /// </summary>
        public static void BuildWebSpike()
        {
            ProjectSetup.Apply();
            SpikeSetup.Apply();
            var ok = BuildWeb(SpikeSetup.Scene2DPath, WebSpikeRoot + "spike-2d");
            ok = BuildWeb(SpikeSetup.Scene3DPath, WebSpikeRoot + "spike-3d") && ok;
            Debug.Log("GV_BUILD web-spike result=" + (ok ? "Succeeded" : "Failed"));
            Finish(ok, "Web spike build failed");
        }

        public const string WebSpikeRoot = "../web/public/unity/";

        private static bool BuildWeb(string scene, string output)
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, true); // never measure stale files
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            Debug.Log($"GV_BUILD web scene={Path.GetFileNameWithoutExtension(scene)} result={summary.result} time={summary.totalTime} errors={summary.totalErrors} path={Path.GetFullPath(output)}");
            return summary.result == BuildResult.Succeeded;
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
}
