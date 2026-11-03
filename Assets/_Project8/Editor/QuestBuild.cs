using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace Project8.EditorTools
{
    /// <summary>
    /// One-shot Android/Quest build configuration + APK packaging.
    /// Run: Unity -batchmode -executeMethod Project8.EditorTools.QuestBuild.BuildQuestApk
    /// </summary>
    public static class QuestBuild
    {
        const string PackageId = "com.ZuydXRGroupA2.Project8GW";
        const string ApkPath = "Builds/Project8-Quest3-v0.8.3.apk";
        const string ExePath = "Builds/Windows/Project8.exe";
        const string AppVersion = "0.8.3";
        const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";

        /// <summary>Everything a Quest APK needs: IL2CPP+ARM64, minSdk 29, package
        /// id, INTERNET permission (AI tutor), and an Android XR manager that
        /// loads OpenXR. The OculusQuestSupport feature itself is enabled in the
        /// OpenXR Package Settings asset.</summary>
        [MenuItem("Project 8/Configure Android for Quest")]
        public static void ConfigureAndroidForQuest()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetApplicationIdentifier(android, PackageId);
            PlayerSettings.bundleVersion = AppVersion;
            PlayerSettings.Android.bundleVersionCode = 11; // 0.8.3 -> code 11
            // AITutor talks to the NVIDIA endpoint over HTTPS; VoiceInteraction
            // uses Microphone (RECORD_AUDIO is auto-added by Unity for mic use
            // and requested at runtime before first recording).
            PlayerSettings.Android.forceInternetPermission = true;

            EnsureAndroidOpenXRLoader();

            AssetDatabase.SaveAssets();
            Debug.Log("[Project8] Android/Quest build configured.");
        }

        /// <summary>XRGeneralSettingsForBuildTarget returns null until an Android
        /// entry exists in the per-build-target asset, so the XRGeneralSettings +
        /// XRManagerSettings pair is created as sub-assets and registered in the
        /// serialized m_SettingsPerBuildTarget list, then the OpenXR loader is
        /// assigned to it.</summary>
        static void EnsureAndroidOpenXRLoader()
        {
            var pbt = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
            if (pbt == null)
            {
                Debug.LogError("[Project8] XRGeneralSettingsPerBuildTarget.asset not found.");
                return;
            }
            var general = XRGeneralSettingsPerBuildTarget
                .XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            XRManagerSettings manager = null;
            if (general == null)
            {
                general = ScriptableObject.CreateInstance<XRGeneralSettings>();
                general.name = "Android Settings";
                general.InitManagerOnStart = true;
                manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = "Android Providers";
                general.Manager = manager;
                AssetDatabase.AddObjectToAsset(manager, pbt);
                AssetDatabase.AddObjectToAsset(general, pbt);

                var so = new SerializedObject(pbt);
                var list = so.FindProperty("m_SettingsPerBuildTarget");
                int i = list.arraySize;
                list.InsertArrayElementAtIndex(i);
                var el = list.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("buildTarget").enumValueFlag =
                    (int)BuildTargetGroup.Android;
                el.FindPropertyRelative("settings").objectReferenceValue = general;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            manager = manager != null ? manager : general.Manager;
            general.InitManagerOnStart = true;

            bool hasOpenXR = manager.activeLoaders
                .OfType<UnityEngine.XR.OpenXR.OpenXRLoader>().Any();
            if (!hasOpenXR)
                XRPackageMetadataStore.AssignLoader(manager,
                    "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);

            EditorUtility.SetDirty(pbt);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
        }

        [MenuItem("Project 8/Build Quest APK")]
        public static bool BuildQuestApk()
        {
            if (System.IO.File.Exists(ApkPath))
            {
                Debug.LogError($"[Project8] Refusing to overwrite existing APK: {ApkPath}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return false;
            }

            ConfigureAndroidForQuest();
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            });
            var summary = report.summary;
            Debug.Log($"[Project8] APK build: {summary.result} - {ApkPath} " +
                      $"({summary.totalSize / (1024f * 1024f):F1} MB)");
            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            return summary.result == BuildResult.Succeeded;
        }

        /// <summary>Windows standalone player - same scenes, desktop fallback
        /// (device simulator) when no headset is present.</summary>
        public static bool BuildWindowsExe()
        {
            PlayerSettings.bundleVersion = AppVersion;
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            });
            var summary = report.summary;
            Debug.Log($"[Project8] EXE build: {summary.result} - {ExePath} " +
                      $"({summary.totalSize / (1024f * 1024f):F1} MB)");
            return summary.result == BuildResult.Succeeded;
        }

        /// <summary>One call builds both installers - used by the in-editor
        /// runner flag so packaging can run without a second Unity instance.</summary>
        [MenuItem("Project 8/Build All (Quest APK + Windows EXE)")]
        public static void BuildAllPlatforms()
        {
            bool exe = BuildWindowsExe();
            bool apk = BuildQuestApk();
            Debug.Log($"[Project8] BuildAll: exe={exe} apk={apk}");
            if (!exe || !apk)
                throw new System.Exception("[Project8] BuildAll: one or more targets failed.");
        }
    }
}
