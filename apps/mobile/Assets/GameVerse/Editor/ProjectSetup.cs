using System.IO;
using GameVerse.Platform;
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
        public const string PanelSettingsPath = Root + "/UI/GameVersePanelSettings.asset";
        public const string ThemePath = Root + "/UI/Theme/GameVerse.tss";
        public const string HomeDocumentPath = Root + "/UI/Home/Home.uxml";
        public const string BootScenePath = Root + "/Scenes/Boot.unity";

        [MenuItem("GameVerse/Apply Project Setup")]
        public static void Apply()
        {
            ConfigurePlayer();
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

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, ApplicationId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;

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

            GraphicsSettings.defaultRenderPipeline = urp;
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
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
