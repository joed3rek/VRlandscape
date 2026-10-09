using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace VRLandscape.EditorTools
{
    // "VR Landscape" menu: one-click project configuration for a single APK that runs on Quest and phones.
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/VRLandscape/Scenes/Main.unity";
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string ARCoreLoader = "UnityEngine.XR.ARCore.ARCoreLoader";

        [MenuItem("VR Landscape/1. Configure Project (Android, Quest + Phone)", priority = 1)]
        public static void Configure()
        {
            if (BlockedByPlayMode()) return;
            CreateMainScene();
            ConfigurePlayer();
            ConfigureXR(BuildTargetGroup.Android);
            ConfigureXR(BuildTargetGroup.Standalone);
            IncludeRequiredShaders();
            AssetDatabase.SaveAssets();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                EditorUtility.DisplayDialog("VR Landscape", "Switch the build platform to Android now? (takes a while the first time)", "Switch", "Later"))
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            Debug.Log("[VRLandscape] Project configured. Press Play to try the phone mode in the editor (WASD + drag to look).");
        }

        [MenuItem("VR Landscape/2. Build APK", priority = 2)]
        public static void BuildApk()
        {
            if (BlockedByPlayMode()) return;
            // OpenXR's Meta Quest build check reads the *selected* platform; on Desktop it hits a null and logs
            // a NullReferenceException. Selecting Android first keeps the build log clean.
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
            Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/VRLandscape.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorUtility.RevealInFinder("Builds/VRLandscape.apk");
        }

        // XR loaders and build settings can't change while the game is running in the editor.
        static bool BlockedByPlayMode()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return false;
            EditorUtility.DisplayDialog("VR Landscape", "Stop Play mode first (press the Play button again), then run this menu.", "OK");
            return true;
        }

        [MenuItem("VR Landscape/Open Device Parks Folder (editor)", priority = 20)]
        public static void OpenParksFolder()
        {
            var dir = Path.Combine(Application.persistentDataPath, "Parks");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir + "/");
        }

        [MenuItem("VR Landscape/Use Headset in Play Mode (Quest Link)", priority = 21)]
        static void ToggleXRInEditor() =>
            EditorPrefs.SetBool(XRBootstrap.EditorPrefKey, !EditorPrefs.GetBool(XRBootstrap.EditorPrefKey, false));

        [MenuItem("VR Landscape/Use Headset in Play Mode (Quest Link)", true)]
        static bool ToggleXRInEditorValidate()
        {
            Menu.SetChecked("VR Landscape/Use Headset in Play Mode (Quest Link)", EditorPrefs.GetBool(XRBootstrap.EditorPrefKey, false));
            return true;
        }

        static void CreateMainScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.companyName = "VR Landscape";
            PlayerSettings.productName = "Park Walkthrough";
            PlayerSettings.SetApplicationIdentifier(android, "com.vrlandscape.parkwalkthrough");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Engine code stripping removes colliders that GameObject.CreatePrimitive needs (demo parks).
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            // ARCore's camera feed needs OpenGL ES 3; Quest runs fine on it too.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            // Active Input Handling = Input System Package (1). "Both" (2) is unsupported on Android.
            // Requires an editor restart if it changed.
            var playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>().FirstOrDefault();
            if (playerSettings != null)
            {
                var so = new SerializedObject(playerSettings);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 1)
                {
                    prop.intValue = 1;
                    so.ApplyModifiedProperties();
                    Debug.LogWarning("[VRLandscape] Switched Active Input Handling to the Input System. Restart the editor.");
                }
            }
        }

        static void ConfigureXR(BuildTargetGroup group)
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (general == null || general.Manager == null)
            {
                Debug.LogWarning($"[VRLandscape] XR settings for {group} don't exist yet. Open Project Settings > XR Plug-in Management " +
                                 $"once (select the {group} tab), then run this menu again.");
                return;
            }

            // XR is started by code: OpenXR only on headsets, ARCore only inside AR Site Walk on phones.
            general.InitManagerOnStart = false;
            XRPackageMetadataStore.AssignLoader(general.Manager, OpenXRLoader, group);
            if (group == BuildTargetGroup.Android)
            {
                XRPackageMetadataStore.AssignLoader(general.Manager, ARCoreLoader, group);
                MakeARCoreOptional();
            }
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
            var perTarget = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            foreach (var guid in perTarget)
                EditorUtility.SetDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));

            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr == null) return;
            foreach (var feature in openxr.GetFeatures())
            {
                string n = feature.GetType().Name;
                if (n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" ||
                    n == "MetaQuestTouchProControllerProfile" || (group == BuildTargetGroup.Android && n == "MetaQuestFeature"))
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                }
            }
            EditorUtility.SetDirty(openxr);
        }

        // "Optional" lets the same APK install on Quest and on phones without ARCore (AR then shows a simulated preview).
        // Done by reflection so this file compiles even if the ARCore package API shifts.
        static void MakeARCoreOptional()
        {
            var type = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEditor.XR.ARCore.ARCoreSettings"))
                .FirstOrDefault(t => t != null);
            var settings = type?.GetMethod("GetOrCreateSettings")?.Invoke(null, null) as Object;
            if (settings == null)
            {
                Debug.LogWarning("[VRLandscape] Set Project Settings > XR Plug-in Management > ARCore > Requirement to Optional by hand.");
                return;
            }
            foreach (var name in new[] { "requirement", "depth" })
            {
                var prop = type.GetProperty(name);
                if (prop == null || !prop.PropertyType.IsEnum) continue;
                prop.SetValue(settings, System.Enum.Parse(prop.PropertyType, "Optional"));
            }
            EditorUtility.SetDirty(settings);
        }

        // Shaders that nothing in the scene references, but the app creates at runtime: UI, the default lit
        // material, TextMesh labels, and glTFast's shaders for models loaded from files. Missing ones render pink.
        static readonly string[] RequiredShaders =
        {
            "UI/Default", "Sprites/Default", "Standard", "Skybox/Procedural",
            "Legacy Shaders/Diffuse", "Hidden/CubeBlur", "Hidden/CubeCopy", "Hidden/CubeBlend",
            "glTF/PbrMetallicRoughness", "glTF/PbrSpecularGlossiness", "glTF/Unlit",
        };

        static void IncludeRequiredShaders()
        {
            var graphics = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(graphics);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;

            // Shaders from "unity default resources" (e.g. GUI/Text Shader) always ship with the player;
            // listing them makes the build fail writing unity_builtin_extra, so drop them and empty slots.
            var present = new System.Collections.Generic.HashSet<Object>();
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var shader = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (shader == null || AssetDatabase.GetAssetPath(shader) == "Library/unity default resources")
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    list.DeleteArrayElementAtIndex(i);
                }
                else present.Add(shader);
            }

            foreach (var name in RequiredShaders)
            {
                var shader = Shader.Find(name);
                if (shader == null) { Debug.LogWarning("[VRLandscape] Shader not found: " + name); continue; }
                if (present.Contains(shader)) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedProperties();
        }
    }
}
