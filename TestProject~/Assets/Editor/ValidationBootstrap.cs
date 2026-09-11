using System;
using System.IO;

using TMPro;

using UnityEditor;

using UnityEngine;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace SuperHeroUnite.UI.Validation
{
    public static class ValidationBootstrap
    {
        public static void EnsureResources()
        {
            try
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;
                if (HasTextResources())
                {
                    Finish();
                    return;
                }

                PackageInfo package = PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                if (package == null)
                {
                    throw new InvalidOperationException("Cannot resolve the installed TMP package.");
                }

                string archive = Path.Combine(
                    package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                if (!File.Exists(archive))
                {
                    throw new FileNotFoundException("TMP Essential Resources archive is missing.", archive);
                }

                AssetDatabase.importPackageCompleted += OnImported;
                AssetDatabase.importPackageFailed += OnImportFailed;
                AssetDatabase.ImportPackage(archive, false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static bool HasTextResources()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(
                "Assets/TextMesh Pro/Resources/TMP Settings.asset");
            return settings != null && TMP_Settings.defaultFontAsset != null;
        }

        private static void OnImported(string packageName)
        {
            EditorApplication.delayCall += Finish;
        }

        private static void OnImportFailed(string packageName, string error)
        {
            Debug.LogError($"Could not import {packageName}: {error}");
            EditorApplication.Exit(1);
        }

        private static void Finish()
        {
            if (!HasTextResources())
            {
                Debug.LogError("TMP Essential Resources did not provide the default font.");
                EditorApplication.Exit(1);
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Super Hero UI validation resources are ready.");
            EditorApplication.Exit(0);
        }
    }
}
