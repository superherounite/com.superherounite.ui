using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipeScalingTests
    {
        private string _testRoot;

        private string ConsumerPath => _testRoot + "/Shared Consumer.prefab";

        [SetUp]
        public void SetUp()
        {
            StyleRecipeProcessor.ClearPreviewCache();
            _testRoot = "Assets/__SuperHeroUIScalingTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_testRoot))
            {
                AssetDatabase.DeleteAsset(_testRoot);
            }

            _testRoot = null;
            AssetDatabase.Refresh();
        }

        [TestCase(2)]
        [TestCase(12)]
        [TestCase(100)]
        public void PreviewLoadsSharedConsumerOnceAndRemainsReadOnly(int ownerCount)
        {
            Fixture fixture = CreateFixture(ownerCount);
            IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();
            var stopwatch = Stopwatch.StartNew();

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            stopwatch.Stop();
            TestContext.WriteLine(
                $"Preview: {ownerCount} owners, one shared consumer, "
                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms, "
                + $"{preview.OwnerPrefabLoadCount} owner loads, "
                + $"{preview.ConsumerPrefabLoadCount} consumer loads.");
            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Stale), preview.ToString());
            Assert.That(preview.Errors, Is.Empty);
            Assert.That(preview.Changes.Count, Is.EqualTo(ownerCount));
            Assert.That(preview.OwnerPrefabLoadCount, Is.EqualTo(ownerCount));
            Assert.That(preview.ConsumerPrefabLoadCount, Is.EqualTo(1));
            AssertFilesUnchanged(before);

            StyleReview repeated = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(repeated.State, Is.EqualTo(preview.State));
            Assert.That(repeated.Assets, Is.EqualTo(preview.Assets));
            Assert.That(repeated.Changes, Is.EqualTo(preview.Changes));
            Assert.That(repeated.Errors, Is.EqualTo(preview.Errors));
            Assert.That(repeated.Fingerprint, Is.EqualTo(preview.Fingerprint));
            Assert.That(repeated.OwnerPrefabLoadCount, Is.Zero);
            Assert.That(repeated.ConsumerPrefabLoadCount, Is.Zero);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void SharedConsumerStillRejectsOverrideOnLaterOwner()
        {
            Fixture fixture = CreateFixture(3, 2);
            IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Error), preview.ToString());
            Assert.That(preview.ConsumerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(
                preview.Errors.Any(error => error.Contains("Owner 002")
                    && error.Contains("overrides a recipe-owned value")),
                Is.True,
                preview.ToString());
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(fixture.Registry, preview));
            AssertFilesUnchanged(before);
        }

        [Test]
        public void SharedConsumerStillRejectsDuplicateRegistrationWithinRecipe()
        {
            Fixture fixture = CreateFixture(3);
            SetPrivateField(
                fixture.Recipes[2],
                "_consumerPrefabs",
                new[] { fixture.ConsumerPrefab, fixture.ConsumerPrefab });
            EditorUtility.SetDirty(fixture.Recipes[2]);
            AssetDatabase.SaveAssets();

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Error), preview.ToString());
            Assert.That(preview.ConsumerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(
                preview.Errors.Count(error => error.Contains("Duplicate consumer Prefab")),
                Is.EqualTo(1),
                preview.ToString());
        }

        [Test]
        public void ApplyUpdatesEveryOwnerAndSharedConsumerThenRemainsIdempotent()
        {
            Fixture fixture = CreateFixture(3);
            byte[] consumerBefore = File.ReadAllBytes(ConsumerPath);
            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);

            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            Assert.That(applied.Errors, Is.Empty);
            Assert.That(applied.Changes, Is.Empty);
            Assert.That(applied.ConsumerPrefabLoadCount, Is.EqualTo(1));
            foreach (GameObject owner in fixture.OwnerPrefabs)
            {
                AssertPrefabColor(AssetDatabase.GetAssetPath(owner), fixture.Color);
            }

            AssertPrefabColor(ConsumerPath, fixture.Color);
            Assert.That(File.ReadAllBytes(ConsumerPath), Is.EqualTo(consumerBefore));
            IReadOnlyDictionary<string, FileSnapshot> afterApply = CaptureFiles();

            StyleReview repeated = StyleRecipeProcessor.Apply(fixture.Registry, applied);

            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready), repeated.ToString());
            Assert.That(repeated.Changes, Is.Empty);
            Assert.That(repeated.Errors, Is.Empty);
            Assert.That(repeated.Fingerprint, Is.EqualTo(applied.Fingerprint));
            AssertFilesUnchanged(afterApply);
        }

        private Fixture CreateFixture(int ownerCount, int overriddenOwnerIndex = -1)
        {
            Color color = new Color(0.25f, 0.5f, 0.75f, 1f);
            ColorToken token = CreateAsset<ColorToken>("Shared Color.asset");
            SetPrivateField(token, "_value", color);
            EditorUtility.SetDirty(token);
            var owners = new GameObject[ownerCount];
            var recipes = new PrefabStyleRecipe[ownerCount];
            for (int index = 0; index < ownerCount; index++)
            {
                string ownerName = $"Owner {index:D3}";
                string ownerPath = _testRoot + "/" + ownerName + ".prefab";
                var root = new GameObject(
                    ownerName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                try
                {
                    Image image = root.GetComponent<Image>();
                    image.color = Color.white;
                    image.raycastTarget = false;
                    ((RectTransform)root.transform).anchoredPosition = new Vector2(11f, 22f);
                    owners[index] = PrefabUtility.SaveAsPrefabAsset(root, ownerPath);
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
                SetPrivateField(binding, "_color", token);
                PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>(ownerName + " Recipe.asset");
                SetPrivateField(recipe, "_ownerPrefab", owners[index]);
                SetPrivateField(recipe, "_graphicColors", new[] { binding });
                recipes[index] = recipe;
            }

            GameObject consumer = CreateConsumer(owners, overriddenOwnerIndex);
            foreach (PrefabStyleRecipe recipe in recipes)
            {
                SetPrivateField(recipe, "_consumerPrefabs", new[] { consumer });
                EditorUtility.SetDirty(recipe);
            }

            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(registry, "_recipes", recipes);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return new Fixture(registry, recipes, owners, consumer, color);
        }

        private GameObject CreateConsumer(GameObject[] owners, int overriddenOwnerIndex)
        {
            var root = new GameObject("Shared Consumer", typeof(RectTransform));
            try
            {
                for (int index = 0; index < owners.Length; index++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(owners[index]);
                    instance.transform.SetParent(root.transform, false);
                    if (index == overriddenOwnerIndex)
                    {
                        Image image = instance.GetComponent<Image>();
                        image.color = Color.black;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                    }
                }

                return PrefabUtility.SaveAsPrefabAsset(root, ConsumerPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertPrefabColor(string path, Color expected)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Image[] images = root.GetComponentsInChildren<Image>(true);
                Assert.That(images, Is.Not.Empty);
                foreach (Image image in images)
                {
                    Assert.That(image.color, Is.EqualTo(expected), path + "/" + image.name);
                    Assert.That(image.raycastTarget, Is.False, "Preserve unmanaged raycast settings.");
                    Assert.That(
                        ((RectTransform)image.transform).anchoredPosition,
                        Is.EqualTo(new Vector2(11f, 22f)),
                        "Preserve unmanaged layout.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private IReadOnlyDictionary<string, FileSnapshot> CaptureFiles()
        {
            return Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => new FileSnapshot(path));
        }

        private void AssertFilesUnchanged(IReadOnlyDictionary<string, FileSnapshot> before)
        {
            IReadOnlyDictionary<string, FileSnapshot> after = CaptureFiles();
            Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
            foreach (KeyValuePair<string, FileSnapshot> pair in before)
            {
                Assert.That(after[pair.Key].Bytes, Is.EqualTo(pair.Value.Bytes), pair.Key);
                Assert.That(
                    after[pair.Key].LastWriteTimeUtc,
                    Is.EqualTo(pair.Value.LastWriteTimeUtc),
                    "Unexpected file write: " + pair.Key);
            }
        }

        private T CreateAsset<T>(string fileName)
            where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, _testRoot + "/" + fileName);
            return asset;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test field {target.GetType().Name}.{fieldName}");
            field.SetValue(target, value);
        }

        private sealed class FileSnapshot
        {
            public byte[] Bytes { get; }
            public DateTime LastWriteTimeUtc { get; }

            public FileSnapshot(string path)
            {
                Bytes = File.ReadAllBytes(path);
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            }
        }

        private sealed class Fixture
        {
            public StyleRecipeRegistry Registry { get; }
            public PrefabStyleRecipe[] Recipes { get; }
            public GameObject[] OwnerPrefabs { get; }
            public GameObject ConsumerPrefab { get; }
            public Color Color { get; }

            public Fixture(
                StyleRecipeRegistry registry,
                PrefabStyleRecipe[] recipes,
                GameObject[] ownerPrefabs,
                GameObject consumerPrefab,
                Color color)
            {
                Registry = registry;
                Recipes = recipes;
                OwnerPrefabs = ownerPrefabs;
                ConsumerPrefab = consumerPrefab;
                Color = color;
            }
        }
    }
}
