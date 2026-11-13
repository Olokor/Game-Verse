using GameVerse.Platform.Diagnostics;
using GameVerse.Platform.Screens;
using GameVerse.Platform.Shell;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GameVerse.Platform
{
    /// <summary>
    /// Entry point of the Boot scene: builds the platform shell (router, screens, overlay) on the UI document and ticks it
    /// every frame. In development builds it first runs the shared-rules self-checks: GameVerse.Rules behaves identically
    /// under IL2CPP (RulesSelfCheck) and error reports reach GlitchTip (ErrorReportingSelfCheck, ADR-0005).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class Boot : MonoBehaviour
    {
        private AppServices services;

        private void Start()
        {
            Application.targetFrameRate = 60;

            if (Debug.isDebugBuild)
            {
                var report = RulesSelfCheck.Run();
                // One parseable line each for `make device-check` (adb logcat -s Unity) and for CI logs.
                Debug.Log(report.ToLogLine());
                Debug.Log(ErrorReportingSelfCheck.Run());
            }

            var root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            root.AddToClassList("gv-root");
            if (Resources.Load<StyleSheet>("Screens/Screens") is { } screens)
            {
                root.styleSheets.Add(screens);
            }

            services = new AppServices(root);
            if (services.Dev.Autoplay)
            {
                Debug.Log("GV_ACCEPT autoplay room=" + services.Dev.AutoplayRoom);
            }

            services.Router.Push(new WelcomeScreen(services));
        }

        private void Update()
        {
            // Android's back button arrives as Escape.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                services?.Router.Back();
            }

            services?.Tick();
        }
    }
}
