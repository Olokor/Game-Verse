using System.IO;
using GameVerse.Platform;
using GameVerse.Platform.Diagnostics;
using Sentry.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GameVerse.Editor
{
    /// <summary>
    /// Every project setting and generated asset the skeleton needs, as code instead of editor clicks (CLAUDE.md: changes
    /// stay diffable). Idempotent: run it again after pulling and it only fills what is missing or wrong.
    ///   Menu: GameVerse > Apply Project Setup
    ///   Batch: Unity -batchmode -quit -projectPath apps/mobile -executeMethod GameVerse.Editor.ProjectSetup.Apply
    /// </summary>
    public static class ProjectSetup
    {
        public const string ApplicationId = "app.gameverse.mobile";
        public const string Root = "Assets/GameVerse";
        public const string RenderingDir = Root + "/Rendering";
        public const string UrpAssetPath = RenderingDir + "/GameVerse-URP.asset";
        public const string Renderer2DPath = RenderingDir + "/GameVerse-Renderer2D.asset";
        public const string ForwardPlusRendererPath = RenderingDir + "/GameVerse-ForwardPlus.asset";
        public const int ForwardPlusRendererIndex = 1;
        public const string PanelSettingsPath = Root + "/UI/GameVersePanelSettings.asset";
        public const string ThemePath = Root + "/UI/Theme/GameVerse.tss";
        public const string HomeDocumentPath = Root + "/UI/Home/Home.uxml";
        public const string BootScenePath = Root + "/Scenes/Boot.unity";

        // Error reporting (ADR-0005). The Sentry Unity SDK loads its options from this exact Resources path.
        public const string SentryOptionsPath = "Assets/Resources/Sentry/SentryOptions.asset";
        public const string SentryConfigurationPath = Root + "/Platform/Diagnostics/GameVerseSentryConfiguration.asset";

        [MenuItem("GameVerse/Apply Project Setup")]
        public static void Apply()
        {
            ConfigurePlayer();
            ConfigureErrorReporting();
            ConfigureRendering();
            var panel = EnsurePanelSettings();
            EnsureBootScene(panel);
            AssetDatabase.SaveAssets();
            // Completion marker read by infrastructure/scripts/unity-batch.mjs.
            Debug.Log("GV_SETUP ok");
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "GameVerse";
            PlayerSettings.productName = "GameVerse";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // No Unity splash (allowed on every licence since Unity 6): the Boot screen carries our own branding and the
            // player sees the game sooner. (Size effect measured in Prompt 0.7: about 0.06 MB, so not a size lever.)
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, ApplicationId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

            // Web defaults, chosen for download size (measured in the Prompt 0.7 spike; ADR-0002). Per platform,
            // so Android is unaffected. Brotli without decompression fallback means the server must send
            // Content-Encoding: br for *.br (see apps/web/vite-plugin-unity-brotli.ts).
            var web = NamedBuildTarget.WebGL;
            PlayerSettings.SetManagedStrippingLevel(web, ManagedStrippingLevel.High);
            PlayerSettings.SetIl2CppCodeGeneration(web, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 }); // WebGL 2 only
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;

            // Active input handling = Input System package only (1). No public API, so set the serialized field.
            var projectSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (projectSettings.Length > 0)
            {
                var so = new SerializedObject(projectSettings[0]);
                var handler = so.FindProperty("activeInputHandler");
                if (handler != null && handler.intValue != 1)
                {
                    handler.intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        /// <summary>
        /// Sentry Unity SDK settings, privacy first (ADR-0005). The DSN stays empty in the committed asset: BuildScript
        /// injects UNITY_ERROR_DSN only for the duration of a build, so no environment's DSN is ever committed.
        /// Also switches off Unity's own analytics and performance reporting, which phone home from every build.
        /// </summary>
        public static ScriptableSentryUnityOptions ConfigureErrorReporting()
        {
            UnityEditor.Analytics.AnalyticsSettings.enabled = false;
            UnityEditor.CrashReporting.CrashReportingSettings.enabled = false;
            UnityEditor.Analytics.PerformanceReportingSettings.enabled = false;

            var configuration = AssetDatabase.LoadAssetAtPath<GameVerseSentryConfiguration>(SentryConfigurationPath);
            if (configuration == null)
            {
                configuration = ScriptableObject.CreateInstance<GameVerseSentryConfiguration>();
                AssetDatabase.CreateAsset(configuration, SentryConfigurationPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SentryOptionsPath));
            var options = AssetDatabase.LoadAssetAtPath<ScriptableSentryUnityOptions>(SentryOptionsPath);
            if (options == null)
            {
                options = ScriptableObject.CreateInstance<ScriptableSentryUnityOptions>();
                AssetDatabase.CreateAsset(options, SentryOptionsPath);
            }

            options.Enabled = true;
            options.Dsn = string.Empty;
            options.CaptureInEditor = false;
            options.SendDefaultPii = false;
            options.AttachScreenshot = false;
            options.AttachViewHierarchy = false;
            options.EnableLogDebouncing = true;
            options.TracesSampleRate = 0;
            options.AutoStartupTraces = false;
            options.Il2CppLineNumberSupportEnabled = true;
            options.AndroidNativeSupportEnabled = true;
            options.NdkIntegrationEnabled = true;
            options.AnrDetectionEnabled = true;
            options.OptionsConfiguration = configuration;
            EditorUtility.SetDirty(options);
            return options;
        }

        private static void ConfigureRendering()
        {
            Directory.CreateDirectory(RenderingDir);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                // 2D Renderer for the shell and 2D games (CLAUDE.md section 3). 3D games add a Forward+ renderer later.
                var renderer = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer, Renderer2DPath);
                urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, UrpAssetPath);
            }

            EnsureForwardPlusRenderer(urp);
            GraphicsSettings.defaultRenderPipeline = urp;
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
        }

        /// <summary>
        /// Renderer 1 on the URP asset: Universal Renderer in Forward+ mode for 3D games (CLAUDE.md section 3). Renderer 0
        /// stays the 2D Renderer used by the shell and 2D games. A 3D scene's camera selects renderer 1.
        /// </summary>
        private static void EnsureForwardPlusRenderer(UniversalRenderPipelineAsset urp)
        {
            var forwardPlus = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ForwardPlusRendererPath);
            if (forwardPlus == null)
            {
                forwardPlus = ScriptableObject.CreateInstance<UniversalRendererData>();
                forwardPlus.renderingMode = RenderingMode.ForwardPlus;
                AssetDatabase.CreateAsset(forwardPlus, ForwardPlusRendererPath);
            }

            // The renderer list has no public setter, so edit the serialized field.
            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            for (var i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == forwardPlus)
                {
                    return;
                }
            }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = forwardPlus;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }

        private static PanelSettings EnsurePanelSettings()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }

            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            // Design target is a 360x800 phone (CLAUDE.md section 9); scale with width so layouts match the web at 360.
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(360, 800);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0f;
            EditorUtility.SetDirty(panel);
            return panel;
        }

        private static void EnsureBootScene(PanelSettings panel)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GameVerse.UI.GameVerseTokens.Color.Bg.Base;
            camera.orthographic = true;
            cameraGo.tag = "MainCamera";

            var boot = new GameObject("Boot");
            var document = boot.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HomeDocumentPath);
            boot.AddComponent<Boot>();

            EditorSceneManager.SaveScene(scene, BootScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }
    }
}
