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
        const string ApkPath = "Builds/Project8-Quest3.apk";
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
            // AITutor talks to the NVIDIA endpoint over HTTPS.
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
        public static void BuildQuestApk()
        {
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
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
