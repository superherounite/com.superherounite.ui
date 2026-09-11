using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipeGuardsTests
    {
        private string _testRoot;
        private StyleRecipeRegistry _registry;
        private ColorToken _token;

        [SetUp]
        public void SetUp()
        {
            _testRoot = "Assets/__SuperHeroUIGuardsTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
            StyleRecipeProcessor.ClearPreviewCache();
        }

        [TearDown]
        public void TearDown()
        {
            StyleRecipeGuards.ClearReady(_registry);
            StyleRecipeProcessor.ClearPreviewCache();
            AssetDatabase.DeleteAsset(_testRoot);
            _registry = null;
            _token = null;
        }

        [Test]
        public void UnchangedReadyCacheSurvivesClearingPreviewCache()
        {
            CreateRegistry(1);
            StyleReview ready = StyleRecipeProcessor.Preview(_registry);
            Assert.That(ready.State, Is.EqualTo(StyleReviewState.Ready), ready.ToString());
            Assert.That(ready.DependencyFingerprint, Is.Not.Empty);
            StyleRecipeGuards.RecordReady(_registry, ready);
            StyleRecipeProcessor.ClearPreviewCache();

            Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.True);
        }

        [Test]
        public void RecordingOlderReadyReviewCannotCertifyAnUnmarkedLaterTokenEdit()
        {
            CreateRegistry(1);
            StyleReview ready = StyleRecipeProcessor.Preview(_registry);
            Assert.That(ready.State, Is.EqualTo(StyleReviewState.Ready), ready.ToString());
            int dirtyCount = EditorUtility.GetDirtyCount(_token);
            SetPrivateField(_token, "_value", Color.magenta);
            Assert.That(EditorUtility.GetDirtyCount(_token), Is.EqualTo(dirtyCount));
            Assert.That(EditorUtility.IsDirty(_token), Is.False);

            StyleRecipeGuards.RecordReady(_registry, ready);

            Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.False);
            Assert.That(StyleRecipeProcessor.Preview(_registry).State, Is.EqualTo(StyleReviewState.Stale));
        }

        [Test]
        public void DirtyAuthoringDependencyInvalidatesReadyCache()
        {
            CreateRegistry(1);
            StyleRecipeGuards.RecordReady(_registry, StyleRecipeProcessor.Preview(_registry));
            Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.True);

            EditorUtility.SetDirty(_token);

            Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.False);
        }

        [Test]
        public void ImportedOwnerChangeInvalidatesSavedReadyCache()
        {
            CreateRegistry(1);
            StyleRecipeGuards.RecordReady(_registry, StyleRecipeProcessor.Preview(_registry));
            Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.True);
            string ownerPath = AssetDatabase.GetAssetPath(_registry.Recipes[0].OwnerPrefab);
            uint dependencyVersion = AssetDatabase.GlobalArtifactDependencyVersion;
            GameObject root = PrefabUtility.LoadPrefabContents(ownerPath);
            try
            {
                root.GetComponent<Image>().color = Color.black;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(root, ownerPath), Is.Not.Null);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            Assert.That(AssetDatabase.GlobalArtifactDependencyVersion, Is.Not.EqualTo(dependencyVersion),
                "Saving a tracked Prefab must immediately invalidate cached artifact dependency hashes.");
            Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.False);
            Assert.That(StyleRecipeProcessor.Preview(_registry).State, Is.EqualTo(StyleReviewState.Stale));
        }

        [Test]
        public void ReadyReviewWithoutValidatedDependencyDigestCannotCreateCache()
        {
            CreateRegistry(1);
            StyleRecipeGuards.RecordReady(_registry, new StyleReview { Registry = _registry });

            Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.False);
        }

        [Test]
        public void LargeSavedRegistryReusesDirectHashesAndReportsWarmGuardCost()
        {
            const int OwnerCount = 100;
            CreateRegistry(OwnerCount);
            StyleReview ready = StyleRecipeProcessor.Preview(_registry);
            Assert.That(ready.State, Is.EqualTo(StyleReviewState.Ready), ready.ToString());
            int hashQueries = 0;
            var fingerprint = new StyleRegistryDependencyFingerprint(
                AssetDatabase.GetAssetPath,
                () => AssetDatabase.GlobalArtifactDependencyVersion,
                path =>
                {
                    hashQueries++;
                    return AssetDatabase.GetAssetDependencyHash(path).ToString();
                },
                target => EditorJsonUtility.ToJson(target));

            Assert.That(fingerprint.Compute(_registry), Is.EqualTo(ready.DependencyFingerprint));
            int coldHashQueries = hashQueries;
            Assert.That(coldHashQueries, Is.EqualTo(OwnerCount * 2 + 2));
            Assert.That(fingerprint.Compute(_registry), Is.EqualTo(ready.DependencyFingerprint));
            Assert.That(hashQueries, Is.EqualTo(coldHashQueries), "Unchanged artifact dependencies reuse each direct asset hash.");
            StyleRecipeGuards.RecordReady(_registry, ready);
            var stopwatch = Stopwatch.StartNew();
            const int CheckCount = 3;
            for (int index = 0; index < CheckCount; index++)
            {
                Assert.That(StyleRecipeGuards.HasReadyCache(_registry), Is.True);
            }

            stopwatch.Stop();
            TestContext.WriteLine(
                $"Warm Play Ready check: {OwnerCount} owners, one shared token, "
                + $"zero repeated native hash queries instead of {coldHashQueries}; "
                + $"{stopwatch.Elapsed.TotalMilliseconds / CheckCount:F2} ms mean over {CheckCount} checks.");
        }

        private void CreateRegistry(int ownerCount)
        {
            _token = CreateAsset<ColorToken>("Shared Color.asset");
            SetPrivateField(_token, "_value", Color.white);
            EditorUtility.SetDirty(_token);
            AssetDatabase.SaveAssetIfDirty(_token);
            var recipes = new PrefabStyleRecipe[ownerCount];
            for (int index = 0; index < ownerCount; index++)
            {
                string ownerPath = _testRoot + $"/Owner {index:D3}.prefab";
                var root = new GameObject("Owner " + index, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                GameObject owner;
                try
                {
                    owner = PrefabUtility.SaveAsPrefabAsset(root, ownerPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                var target = new PrefabTargetReference();
                GameObject loadedOwner = PrefabUtility.LoadPrefabContents(ownerPath);
                try
                {
                    Image image = loadedOwner.GetComponent<Image>();
                    target.Configure(
                        GlobalObjectId.GetGlobalObjectIdSlow(image).ToString(),
                        PrefabTargetResolver.GetDisplayPath(loadedOwner.transform, image.transform),
                        typeof(Image).FullName,
                        PrefabTargetKind.Graphic);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(loadedOwner);
                }

                var binding = new PrefabStyleRecipe.GraphicColorBinding();
                SetPrivateField(binding, "_target", target);
                SetPrivateField(binding, "_color", _token);
                PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>($"Recipe {index:D3}.asset");
                SetPrivateField(recipe, "_ownerPrefab", owner);
                SetPrivateField(recipe, "_graphicColors", new[] { binding });
                EditorUtility.SetDirty(recipe);
                AssetDatabase.SaveAssetIfDirty(recipe);
                recipes[index] = recipe;
            }

            _registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(_registry, "_recipes", recipes);
            EditorUtility.SetDirty(_registry);
            AssetDatabase.SaveAssetIfDirty(_registry);
        }

        private T CreateAsset<T>(string name) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, _testRoot + "/" + name);
            return asset;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
