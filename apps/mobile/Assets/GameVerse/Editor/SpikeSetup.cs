using System.Collections.Generic;
using System.IO;
using GameVerse.Spike;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GameVerse.Editor
{
    /// <summary>
    /// Prompt 0.7 web performance spike (throwaway, deleted with ADR-0002). Generates the two measured scenes in code:
    ///   Spike2D: 200 flipbook-animated sprites (2D Renderer).
    ///   Spike3D: a car lapping an oval track, static scenery with baked lightmaps (Forward+ renderer).
    /// Menu: GameVerse > Spike > Generate Scenes. Called by BuildScript.BuildWebSpike.
    /// </summary>
    public static class SpikeSetup
    {
        public const string Dir = ProjectSetup.Root + "/Spike";
        public const string GeneratedDir = Dir + "/Generated";
        public const string Scene2DPath = Dir + "/Spike2D.unity";
        public const string Scene3DPath = Dir + "/Spike3D.unity";
        private const string SheetPath = GeneratedDir + "/swarm-sheet.png";
        private const string LightingPath = GeneratedDir + "/Spike3D-Lighting.lighting";

        private const int Frames = 4;
        private const int FrameSize = 64;
        private const float RadiusX = 24f;
        private const float RadiusZ = 14f;
        private const float TrackHalfWidth = 3f;

        [MenuItem("GameVerse/Spike/Generate Scenes")]
        public static void Apply()
        {
            Directory.CreateDirectory(GeneratedDir);
            var sheet = EnsureSheet();
            Create2DScene(sheet);
            if (!Create3DScene())
            {
                throw new System.InvalidOperationException("Spike3D lightmap bake failed");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("GV_SPIKE scenes generated");
        }

        // ---------- 2D ----------

        /// <summary>4 frames of a ring with a gap that steps a quarter turn per frame, white on transparent (tinted per sprite).</summary>
        private static Texture2D EnsureSheet()
        {
            var tex = new Texture2D(FrameSize * Frames, FrameSize, TextureFormat.RGBA32, false);
            var c = (FrameSize - 1) / 2f;
            for (var f = 0; f < Frames; f++)
            {
                var gapAngle = f * Mathf.PI / 2f;
                for (var y = 0; y < FrameSize; y++)
                {
                    for (var x = 0; x < FrameSize; x++)
                    {
                        var dx = x - c;
                        var dy = y - c;
                        var r = Mathf.Sqrt(dx * dx + dy * dy);
                        var ring = Mathf.Clamp01(1f - Mathf.Abs(r - 22f) / 6f) * 1.5f;
                        var core = Mathf.Clamp01(1f - r / (8f + 2f * f));
                        var a = Mathf.Atan2(dy, dx);
                        var delta = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gapAngle * Mathf.Rad2Deg));
                        if (delta < 35f)
                        {
                            ring = 0f;
                        }

                        var alpha = Mathf.Clamp01(Mathf.Max(ring, core));
                        tex.SetPixel(f * FrameSize + x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
            }

            File.WriteAllBytes(SheetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
        }

        private static void Create2DScene(Texture2D sheet)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GameVerse.UI.GameVerseTokens.Color.Bg.Base;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.gameObject.AddComponent<SpikeFrameReporter>();

            var light = new GameObject("Global Light 2D").AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;

            var swarm = new GameObject("Swarm").AddComponent<SpriteSwarm>();
            var so = new SerializedObject(swarm);
            so.FindProperty("sheet").objectReferenceValue = sheet;
            so.FindProperty("frameCount").intValue = Frames;
            so.FindProperty("count").intValue = 200;
            var tints = new Color[]
            {
                GameVerse.UI.GameVerseTokens.Color.Category.Board,
                GameVerse.UI.GameVerseTokens.Color.Category.Cards,
                GameVerse.UI.GameVerseTokens.Color.Category.Arcade,
                GameVerse.UI.GameVerseTokens.Color.Category.Racing,
                GameVerse.UI.GameVerseTokens.Color.Category.Fight,
                GameVerse.UI.GameVerseTokens.Color.Category.Adventure,
                GameVerse.UI.GameVerseTokens.Color.Category.Brain,
            };
            var tintProp = so.FindProperty("tints");
            tintProp.arraySize = tints.Length;
            for (var i = 0; i < tints.Length; i++)
            {
                tintProp.GetArrayElementAtIndex(i).colorValue = tints[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, Scene2DPath);
        }

        // ---------- 3D ----------

        private static bool Create3DScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = new Color(0.55f, 0.72f, 0.92f);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.5f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.18f);

            var asphalt = Mat("asphalt", new Color(0.2f, 0.21f, 0.24f), 0.3f);
            var grass = Mat("grass", new Color(0.24f, 0.5f, 0.22f), 0.1f);
            var kerbRed = Mat("kerb-red", new Color(0.85f, 0.18f, 0.16f), 0.3f);
            var kerbWhite = Mat("kerb-white", new Color(0.92f, 0.92f, 0.9f), 0.3f);
            var trunk = Mat("trunk", new Color(0.36f, 0.25f, 0.16f), 0.1f);
            var leaves = Mat("leaves", new Color(0.16f, 0.4f, 0.2f), 0.1f);
            var body = Mat("car-body", GameVerse.UI.GameVerseTokens.Color.Category.Racing, 0.7f);
            var dark = Mat("car-dark", new Color(0.08f, 0.08f, 0.1f), 0.4f);

            var world = new GameObject("World (static)");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8f, 1f, 6f);
            Static(ground, grass, world.transform);

            var track = new GameObject("Track");
            track.AddComponent<MeshFilter>().sharedMesh = TrackMesh();
            Static(track, asphalt, world.transform);

            // Kerbs on both edges, alternating colours.
            const int kerbs = 64;
            for (var i = 0; i < kerbs; i++)
            {
                var t = (i + 0.5f) / kerbs * Mathf.PI * 2f;
                foreach (var offset in new[] { -TrackHalfWidth - 0.3f, TrackHalfWidth + 0.3f })
                {
                    var p = Ellipse(t, offset);
                    var tangent = Tangent(t);
                    var kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    kerb.name = "Kerb";
                    kerb.transform.SetPositionAndRotation(p + Vector3.up * 0.05f, Quaternion.LookRotation(tangent));
                    kerb.transform.localScale = new Vector3(0.6f, 0.1f, 2f * Mathf.PI * Mathf.Max(RadiusX, RadiusZ) / kerbs);
                    Static(kerb, i % 2 == 0 ? kerbRed : kerbWhite, world.transform);
                }
            }

            // Trees outside the track give the bake shadows worth seeing.
            var random = new System.Random(11);
            for (var i = 0; i < 28; i++)
            {
                var t = (float)random.NextDouble() * Mathf.PI * 2f;
                var p = Ellipse(t, TrackHalfWidth + 4f + (float)random.NextDouble() * 6f);
                var tree = new GameObject("Tree");
                tree.transform.SetParent(world.transform, false);
                tree.transform.position = p;
                var height = 2f + (float)random.NextDouble() * 2f;
                var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stem.transform.localPosition = new Vector3(0f, height / 2f, 0f);
                stem.transform.localScale = new Vector3(0.35f, height / 2f, 0.35f);
                Static(stem, trunk, tree.transform, false);
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.transform.localPosition = new Vector3(0f, height + 0.8f, 0f);
                crown.transform.localScale = Vector3.one * (2f + (float)random.NextDouble());
                Static(crown, leaves, tree.transform, false);
            }

            // Dynamic car: realtime direct light + light probes, the usual mobile setup next to baked scenery.
            var car = new GameObject("Car");
            Part(car, PrimitiveType.Cube, body, new Vector3(0f, 0.55f, 0f), new Vector3(1.8f, 0.5f, 4f));
            Part(car, PrimitiveType.Cube, dark, new Vector3(0f, 1f, -0.3f), new Vector3(1.4f, 0.45f, 1.8f));
            foreach (var x in new[] { -0.95f, 0.95f })
            {
                foreach (var z in new[] { -1.3f, 1.3f })
                {
                    var wheel = Part(car, PrimitiveType.Cylinder, dark, new Vector3(x, 0.35f, z), new Vector3(0.7f, 0.15f, 0.7f));
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }

            var probes = new GameObject("Light Probes").AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            for (var i = 0; i < 32; i++)
            {
                var t = i / 32f * Mathf.PI * 2f;
                positions.Add(Ellipse(t, 0f) + Vector3.up * 0.5f);
                positions.Add(Ellipse(t, 0f) + Vector3.up * 2.5f);
            }

            probes.probePositions = positions.ToArray();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = sky;
            camera.fieldOfView = 60f;
            camera.farClipPlane = 200f;
            cameraGo.transform.position = new Vector3(RadiusX, 5f, -10f);
            cameraGo.AddComponent<UniversalAdditionalCameraData>().SetRenderer(ProjectSetup.ForwardPlusRendererIndex);
            cameraGo.AddComponent<SpikeFrameReporter>();

            var drive = car.AddComponent<CarOnTrack>();
            var so = new SerializedObject(drive);
            so.FindProperty("radiusX").floatValue = RadiusX;
            so.FindProperty("radiusZ").floatValue = RadiusZ;
            so.FindProperty("chaseCamera").objectReferenceValue = cameraGo.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, Scene3DPath);

            // Subtractive mixed lighting: static scenery fully baked (light + shadows in lightmaps), the car lit in
            // realtime. CPU lightmapper so the bake also runs headless.
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
            if (settings == null)
            {
                settings = new LightingSettings();
                AssetDatabase.CreateAsset(settings, LightingPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.Subtractive;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 8f;
            settings.lightmapPadding = 2;
            settings.lightmapMaxSize = 1024;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.ao = true;
            settings.aoMaxDistance = 1f;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 128;
            settings.maxBounces = 2;
            settings.lightmapCompression = LightmapCompression.NormalQuality;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            var baked = Lightmapping.Bake();
            EditorSceneManager.SaveScene(scene, Scene3DPath);
            Debug.Log("GV_SPIKE bake " + (baked ? "ok" : "FAILED") + " lightmaps=" + LightmapSettings.lightmaps.Length);
            return baked && LightmapSettings.lightmaps.Length > 0;
        }

        private static Vector3 Ellipse(float t, float normalOffset)
        {
            var p = new Vector3(RadiusX * Mathf.Cos(t), 0f, RadiusZ * Mathf.Sin(t));
            var tangent = Tangent(t);
            var outward = new Vector3(tangent.z, 0f, -tangent.x);
            return p + outward * normalOffset;
        }

        private static Vector3 Tangent(float t)
        {
            return new Vector3(-RadiusX * Mathf.Sin(t), 0f, RadiusZ * Mathf.Cos(t)).normalized;
        }

        private static Mesh TrackMesh()
        {
            const int segments = 128;
            var vertices = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (var i = 0; i <= segments; i++)
            {
                var t = i / (float)segments * Mathf.PI * 2f;
                vertices[i * 2] = Ellipse(t, -TrackHalfWidth) + Vector3.up * 0.02f;
                vertices[i * 2 + 1] = Ellipse(t, TrackHalfWidth) + Vector3.up * 0.02f;
                uvs[i * 2] = new Vector2(i / 4f, 0f);
                uvs[i * 2 + 1] = new Vector2(i / 4f, 1f);
                if (i < segments)
                {
                    var v = i * 2;
                    var k = i * 6;
                    triangles[k] = v;
                    triangles[k + 1] = v + 2;
                    triangles[k + 2] = v + 1;
                    triangles[k + 3] = v + 1;
                    triangles[k + 4] = v + 2;
                    triangles[k + 5] = v + 3;
                }
            }

            var mesh = new Mesh { name = "Track", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            if (Vector3.Dot(mesh.normals[0], Vector3.up) < 0f)
            {
                System.Array.Reverse(triangles);
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(mesh);
            var path = GeneratedDir + "/track.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material Mat(string name, Color color, float smoothness)
        {
            var path = GeneratedDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Static(GameObject go, Material mat, Transform parent, bool keepWorld = true)
        {
            go.transform.SetParent(parent, keepWorld);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = go.AddComponent<MeshRenderer>();
            }

            renderer.sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
        }

        private static GameObject Part(GameObject parent, PrimitiveType type, Material mat, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
