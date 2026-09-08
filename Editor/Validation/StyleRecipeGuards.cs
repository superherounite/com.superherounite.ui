using System;
using System.IO;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    [InitializeOnLoad]
    public sealed class StyleRecipeGuards : IPreprocessBuildWithReport
    {
        private const string CacheVersion = "1";
        private static readonly string s_cacheDirectory = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
            "Library",
            "SuperHeroUI",
            "Ready");

        static StyleRecipeGuards()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (StyleRecipeRegistry registry in FindRegistries())
            {
                if (!registry.ValidateBeforeBuild)
                {
                    continue;
                }

                StyleReview review = StyleRecipeProcessor.Preview(registry);
                if (review.State != StyleReviewState.Ready)
                {
                    ClearReady(registry);
                    throw new BuildFailedException(
                        $"Super Hero UI blocked the build because {registry.name} is "
                        + $"{review.State}.\n{review}");
                }

                RecordReady(registry);
            }
        }

        internal static void RecordReady(StyleRecipeRegistry registry)
        {
            if (registry == null || StyleRecipeProcessor.HasUnsavedDependencies(registry))
            {
                ClearReady(registry);
                return;
            }

            try
            {
                Directory.CreateDirectory(s_cacheDirectory);
                File.WriteAllText(
                    GetCachePath(registry),
                    GetCacheValue(registry));
            }
            catch (IOException exception)
            {
                Debug.LogWarning(
                    $"Super Hero UI could not save its Ready cache: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogWarning(
                    $"Super Hero UI could not save its Ready cache: {exception.Message}");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            foreach (StyleRecipeRegistry registry in FindRegistries())
            {
                if (!registry.ValidateBeforePlay || HasReadyCache(registry))
                {
                    continue;
                }

                StyleReview review;
                try
                {
                    review = StyleRecipeProcessor.Preview(registry);
                }
                catch (InvalidOperationException exception)
                {
                    EditorApplication.isPlaying = false;
                    Debug.LogError(
                        $"Super Hero UI could not validate {registry.name}: {exception.Message}");
                    return;
                }

                if (review.State == StyleReviewState.Ready)
                {
                    RecordReady(registry);
                    continue;
                }

                ClearReady(registry);
                EditorApplication.isPlaying = false;
                Debug.LogError(
                    $"Super Hero UI blocked Play Mode because {registry.name} is "
                    + $"{review.State}.\n{review}");
                EditorApplication.delayCall += () => StyleRecipeWindow.Open(registry, review);
                return;
            }
        }

        private static bool HasReadyCache(StyleRecipeRegistry registry)
        {
            if (StyleRecipeProcessor.HasUnsavedDependencies(registry))
            {
                return false;
            }

            string path = GetCachePath(registry);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    File.ReadAllText(path),
                    GetCacheValue(registry),
                    StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void ClearReady(StyleRecipeRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            try
            {
                string path = GetCachePath(registry);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException exception)
            {
                Debug.LogWarning(
                    $"Super Hero UI could not clear its Ready cache: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogWarning(
                    $"Super Hero UI could not clear its Ready cache: {exception.Message}");
            }
        }

        private static string GetCacheValue(StyleRecipeRegistry registry)
        {
            return CacheVersion + "\n" + StyleRecipeProcessor.GetDependencyFingerprint(registry);
        }

        private static string GetCachePath(StyleRecipeRegistry registry)
        {
            string path = AssetDatabase.GetAssetPath(registry);
            string guid = AssetDatabase.AssetPathToGUID(path);
            return Path.Combine(s_cacheDirectory, guid + ".txt");
        }

        private static StyleRecipeRegistry[] FindRegistries()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:StyleRecipeRegistry",
                new[] { "Assets" });
            Array.Sort(guids, StringComparer.Ordinal);
            var registries = new StyleRecipeRegistry[guids.Length];
            for (int index = 0; index < guids.Length; index++)
            {
                registries[index] = AssetDatabase.LoadAssetAtPath<StyleRecipeRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[index]));
            }

            return registries;
        }
    }
}
