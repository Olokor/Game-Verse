using GameVerse.Platform.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameVerse.Platform
{
    /// <summary>
    /// Entry point of the Boot scene: shows the Home UI document and, in development builds, runs the shared-rules
    /// self-check so a device build proves GameVerse.Rules behaves identically under IL2CPP (see RulesSelfCheck).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class Boot : MonoBehaviour
    {
        private void Start()
        {
            Application.targetFrameRate = 60;

            if (Debug.isDebugBuild)
            {
                var report = RulesSelfCheck.Run();
                // One parseable line for `make device-check` (adb logcat -s Unity) and for CI logs.
                Debug.Log(report.ToLogLine());
            }
        }
    }
}
