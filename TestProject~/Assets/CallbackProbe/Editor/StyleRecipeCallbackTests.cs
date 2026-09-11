using System;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipeCallbackTests
    {
        private string _testRoot;
        private Type _probeType;

        [SetUp]
        public void SetUp()
        {
            StyleRecipeProcessor.ClearPreviewCache();
            _probeType = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                .FirstOrDefault(type => type.FullName
                    == "SuperHeroUnite.UI.CallbackProbe.PrefabInspectionCallbackProbe");
            Assert.That(_probeType, Is.Not.Null, "The temporary runtime callback probe must be imported.");
            ResetProbeState();
            _testRoot = "Assets/__SuperHeroUICallbackTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            StyleRecipeProcessor.ClearPreviewCache();
            if (_probeType != null)
            {
                ResetProbeState();
            }
            if (!string.IsNullOrEmpty(_testRoot))
            {
                AssetDatabase.DeleteAsset(_testRoot);
            }

            _testRoot = null;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CachedPreviewMatchesFullInspectionAfterExecuteAlwaysStateChanges(bool consumerProbe)
        {
            StyleRecipeRegistry registry = CreateFixture(consumerProbe);
            string ownerPath = _testRoot + "/Owner.prefab";
            byte[] before = File.ReadAllBytes(ownerPath);
            string consumerPath = _testRoot + "/Consumer.prefab";
            byte[] consumerBefore = consumerProbe ? File.ReadAllBytes(consumerPath) : null;
            int initialCount = GetEnableCount();
            StyleReview initial = StyleRecipeProcessor.Preview(registry);
            int initialCallbacks = GetEnableCount() - initialCount;
            Assert.That(initial.State, Is.EqualTo(StyleReviewState.Ready), initial.ToString());
            AssertPlayReadyCacheIsNotWritten(registry, initial);

            int warmCount = GetEnableCount();
            StyleReview warm = StyleRecipeProcessor.Preview(registry);
            int warmCallbacks = GetEnableCount() - warmCount;
            Assert.That(warm.State, Is.EqualTo(StyleReviewState.Ready), warm.ToString());
            string dependencyFingerprint = StyleRecipeProcessor.GetDependencyFingerprint(registry);

            SetProbeColor(Color.red);
            Assert.That(
                StyleRecipeProcessor.GetDependencyFingerprint(registry),
                Is.EqualTo(dependencyFingerprint),
                "The probe must change only callback state, without changing serialized dependencies.");

            int incrementalCount = GetEnableCount();
            StyleReview incremental = StyleRecipeProcessor.Preview(registry);
            int incrementalCallbacks = GetEnableCount() - incrementalCount;
            int fullCount = GetEnableCount();
            StyleReview full = StyleRecipeProcessor.PreviewFull(registry);
            int fullCallbacks = GetEnableCount() - fullCount;

            TestContext.WriteLine(
                $"ExecuteAlways {(consumerProbe ? "consumer" : "owner")} probe OnEnable calls: "
                + $"initial={initialCallbacks}, warm={warmCallbacks}, "
                + $"changed incremental={incrementalCallbacks}, full={fullCallbacks}.");
            TestContext.WriteLine(
                $"ExecuteAlways probe results: incremental={incremental.State} "
                + $"({incremental.OwnerPrefabLoadCount} owner loads, "
                + $"{incremental.ConsumerPrefabLoadCount} consumer loads, "
                + $"{incremental.Changes.Count} changes, {incremental.Errors.Count} errors), "
                + $"full={full.State} ({full.OwnerPrefabLoadCount} owner loads, "
                + $"{full.ConsumerPrefabLoadCount} consumer loads, "
                + $"{full.Changes.Count} changes, {full.Errors.Count} errors).");
            if (consumerProbe)
            {
                LogConsumerMutation(consumerPath);
                Assert.That(File.ReadAllBytes(consumerPath), Is.EqualTo(consumerBefore));
            }

            Assert.That(fullCallbacks, Is.GreaterThan(0), "Full inspection must execute the runtime probe.");
            Assert.That(File.ReadAllBytes(ownerPath), Is.EqualTo(before), "Inspection must not save the Prefab.");
            Assert.That(incremental.State, Is.EqualTo(full.State), full.ToString());
            Assert.That(incremental.Assets, Is.EqualTo(full.Assets));
            Assert.That(incremental.Changes, Is.EqualTo(full.Changes));
            Assert.That(incremental.Errors, Is.EqualTo(full.Errors));
            Assert.That(incremental.Fingerprint, Is.EqualTo(full.Fingerprint));
        }

        private StyleRecipeRegistry CreateFixture(bool consumerProbe)
        {
            string ownerPath = _testRoot + "/Owner.prefab";
            var root = new GameObject(
                "Callback Owner",
                typeof(RectTransform),
                typeof(Image));
            GameObject owner;
            try
            {
                if (!consumerProbe)
                {
                    Assert.That(root.AddComponent(_probeType), Is.Not.Null);
                }

                owner = PrefabUtility.SaveAsPrefabAsset(root, ownerPath);
                Assert.That(owner, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            var target = new PrefabTargetReference();
            GameObject loadedOwner = PrefabUtility.LoadPrefabContents(ownerPath);
            try
            {
                Assert.That(
                    loadedOwner.GetComponent(_probeType) != null,
                    Is.EqualTo(!consumerProbe),
                    "The consumer case must keep the owner free of callbacks.");
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

            ColorToken token = CreateAsset<ColorToken>("Color.asset");
            var binding = new PrefabStyleRecipe.GraphicColorBinding();
            SetPrivateField(binding, "_target", target);
            SetPrivateField(binding, "_color", token);
            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>("Recipe.asset");
            SetPrivateField(recipe, "_ownerPrefab", owner);
            SetPrivateField(recipe, "_graphicColors", new[] { binding });
            if (consumerProbe)
            {
                SetPrivateField(recipe, "_consumerPrefabs", new[] { CreateConsumer(owner) });
            }

            EditorUtility.SetDirty(recipe);
            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(registry, "_recipes", new[] { recipe });
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return registry;
        }

        private GameObject CreateConsumer(GameObject owner)
        {
            var root = new GameObject("Callback Consumer", typeof(RectTransform));
            try
            {
                PrefabUtility.InstantiatePrefab(owner, root.transform);
                Assert.That(root.AddComponent(_probeType), Is.Not.Null);
                GameObject consumer = PrefabUtility.SaveAsPrefabAsset(root, _testRoot + "/Consumer.prefab");
                Assert.That(consumer, Is.Not.Null);
                return consumer;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertPlayReadyCacheIsNotWritten(
            StyleRecipeRegistry registry,
            StyleReview review)
        {
            Assert.That(
                StyleRecipeProcessor.HasUnsavedDependencies(registry),
                Is.False,
                "The callback guard fixture must have clean saved dependencies.");
            StyleRecipeGuards.RecordReady(registry, review);
            MethodInfo hasReadyCache = typeof(StyleRecipeGuards).GetMethod(
                "HasReadyCache",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo getCachePath = typeof(StyleRecipeGuards).GetMethod(
                "GetCachePath",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(hasReadyCache, Is.Not.Null);
            Assert.That(getCachePath, Is.Not.Null);
            bool accepted = (bool)hasReadyCache.Invoke(null, new object[] { registry });
            string cachePath = (string)getCachePath.Invoke(null, new object[] { registry });
            bool fileWritten = File.Exists(cachePath);
            TestContext.WriteLine(
                $"ExecuteAlways Play Ready cache: accepted={accepted}, fileWritten={fileWritten}.");
            Assert.That(accepted, Is.False, "Callbacks must bypass the Play Ready cache.");
            Assert.That(fileWritten, Is.False, "Callbacks must not produce a reusable Ready cache file.");
        }

        private static void LogConsumerMutation(string consumerPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(consumerPath);
            try
            {
                Image image = root.GetComponentInChildren<Image>(true);
                var serializedObject = new SerializedObject(image);
                SerializedProperty color = serializedObject.FindProperty("m_Color");
                TestContext.WriteLine(
                    $"ExecuteAlways consumer callback result: managed color={image.color}, "
                    + $"serialized color={color.colorValue}, prefabOverride={color.prefabOverride}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private int GetEnableCount()
        {
            return (int)_probeType.GetProperty("EnableCount").GetValue(null);
        }

        private void SetProbeColor(Color color)
        {
            _probeType.GetProperty("ColorValue").SetValue(null, color);
        }

        private void ResetProbeState()
        {
            _probeType.GetMethod("ResetState").Invoke(null, null);
        }

        private T CreateAsset<T>(string name)
            where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, _testRoot + "/" + name);
            return asset;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing fixture field {target.GetType().Name}.{name}");
            field.SetValue(target, value);
        }
    }
}
