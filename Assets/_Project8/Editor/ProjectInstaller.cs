using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace Project8.EditorTools
{
    /// <summary>
    /// Installs the XR package set (OpenXR, XR Interaction Toolkit, Input System, uGUI)
    /// and imports the XRI "Starter Assets" sample that contains the ready-made
    /// XR Interaction Setup prefab and XRI Default Input Actions.
    ///
    /// Designed to run in batch mode:  Unity.exe -batchmode -projectPath <p>
    ///   -executeMethod Project8.EditorTools.ProjectInstaller.InstallPackages
    /// (no -quit: the method starts async requests, then exits the editor itself).
    /// </summary>
    public static class ProjectInstaller
    {
        static readonly string[] RequiredPackages =
        {
            "com.unity.ugui",
            "com.unity.inputsystem",
            "com.unity.xr.core-utils",
            "com.unity.xr.management",
            "com.unity.xr.openxr",
            "com.unity.xr.interaction.toolkit",
        };

        static Queue<string> _queue;
        static AddRequest _current;

        [MenuItem("Project 8/Install XR Packages")]
        public static void InstallPackages()
        {
            _queue = new Queue<string>(RequiredPackages);
            AddNext();
        }

        static void AddNext()
        {
            if (_queue.Count == 0)
            {
                Debug.Log("[Project8] All packages installed. Importing XRI Starter Assets...");
                ImportStarterAssets();
                Finish();
                return;
            }
            string id = _queue.Dequeue();
            Debug.Log($"[Project8] Installing package: {id}");
            _current = Client.Add(id);
            EditorApplication.update += WaitForCurrent;
        }

        static void WaitForCurrent()
        {
            if (!_current.IsCompleted) return;
            EditorApplication.update -= WaitForCurrent;
            if (_current.Status == StatusCode.Success)
                Debug.Log($"[Project8] Installed {_current.Result.packageId}");
            else
                Debug.LogWarning($"[Project8] Failed: {_current.Error?.message}");
            AddNext();
        }

        static void ImportStarterAssets()
        {
            try
            {
                var samples = Sample.FindByPackage("com.unity.xr.interaction.toolkit", null);
                var starter = samples.FirstOrDefault(s =>
                    s.displayName.IndexOf("Starter", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrEmpty(starter.displayName))
                {
                    starter.Import(Sample.ImportOptions.OverridePreviousImports);
                    AssetDatabase.Refresh();
                    Debug.Log($"[Project8] Imported sample: {starter.displayName}");
                }
                else
                {
                    Debug.LogWarning("[Project8] 'Starter Assets' sample not found in XRI package.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Project8] Sample import failed: {e.Message}");
            }
        }

        static void Finish()
        {
            Debug.Log("[Project8] Package setup complete.");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }
    }
}
