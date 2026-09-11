using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleAutomaticConsumerTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = "Assets/__SuperHeroUIAutomaticConsumerTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_testRoot);
            StyleRecipeProcessor.ClearPreviewCache();
        }

        [Test]
        public void StaleRegistrationValidatesBothCurrentOwnersWithoutWritingAndApplyIsIdempotent()
        {
            Fixture fixture = CreateFixture();
            Dictionary<string, byte[]> before = CaptureFiles();

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview full = StyleRecipeProcessor.PreviewFull(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Ready), preview.ToString());
            Assert.That(full.State, Is.EqualTo(StyleReviewState.Ready), full.ToString());
            Assert.That(preview.Changes, Is.Empty);
            Assert.That(preview.Errors, Is.Empty);
            Assert.That(fixture.Previous.Recipe.ConsumerPrefabs, Is.EqualTo(new[] { fixture.Consumer }));
            Assert.That(fixture.First.Recipe.ConsumerPrefabs, Is.Empty);
            Assert.That(fixture.Second.Recipe.ConsumerPrefabs, Is.Empty);
            AssertFilesUnchanged(before);

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);
            StyleReview repeated = StyleRecipeProcessor.Apply(fixture.Registry, applied);

            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready), repeated.ToString());
            Assert.That(repeated.Changes, Is.Empty);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void OverrideOnSecondReplacementOwnerIsStillAnErrorAndCanBeReviewedForRepair()
        {
            Fixture fixture = CreateFixture();
            OverrideConsumerColor(fixture.Consumer, 1, false, Color.magenta);
            string firstBefore = File.ReadAllText(fixture.First.Path);
            string secondBefore = File.ReadAllText(fixture.Second.Path);

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Error), preview.ToString());
            Assert.That(preview.Errors, Has.Some.Contains("overrides a recipe-owned value"));
            Assert.That(preview.CanPreviewOverrideRepairs, Is.True);
            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Stale), repairs.ToString());

            StyleReview repaired = StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs);

            Assert.That(repaired.State, Is.EqualTo(StyleReviewState.Ready), repaired.ToString());
            Assert.That(File.ReadAllText(fixture.First.Path), Is.EqualTo(firstBefore));
            Assert.That(File.ReadAllText(fixture.Second.Path), Is.EqualTo(secondBefore));
            AssertConsumerValues(fixture);
        }

        [Test]
        public void UnmarkedReplacementBindingChangeRechecksPreviouslyUnmanagedOverride()
        {
            Fixture fixture = CreateFixture();
            ColorToken childToken = CreateAsset<ColorToken>("Child Color.asset");
            SetPrivateField(childToken, "_value", Color.white);
            EditorUtility.SetDirty(childToken);
            AssetDatabase.SaveAssets();
            PrefabStyleRecipe.GraphicColorBinding childBinding = CreateBinding(
                fixture.First.Path, "Unmanaged Child", childToken);
            OverrideConsumerColor(fixture.Consumer, 0, true, Color.magenta);
            Assert.That(EditorUtility.IsDirty(fixture.First.Recipe), Is.False);
            StyleReview initial = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview warm = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(initial.State, Is.EqualTo(StyleReviewState.Ready), initial.ToString());
            Assert.That(warm.State, Is.EqualTo(StyleReviewState.Ready), warm.ToString());
            Dictionary<string, byte[]> before = CaptureFiles();

            SetPrivateField(fixture.First.Recipe, "_graphicColors", new[]
            {
                fixture.First.Recipe.GraphicColors[0],
                childBinding,
            });
            Assert.That(EditorUtility.IsDirty(fixture.First.Recipe), Is.False);
            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(changed.State, Is.EqualTo(StyleReviewState.Error), changed.ToString());
            Assert.That(changed.Errors, Has.Some.Contains("Unmanaged Child"));
            Assert.That(changed.CanPreviewOverrideRepairs, Is.True);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void AssetReferenceWithoutActualRegisteredOwnerInstanceRemainsAnError()
        {
            Fixture fixture = CreateFixture();
            var root = new GameObject("Reference Only", typeof(RectTransform), typeof(Button));
            GameObject referenceOnly;
            try
            {
                root.GetComponent<Button>().targetGraphic = fixture.First.Prefab.GetComponent<Image>();
                referenceOnly = PrefabUtility.SaveAsPrefabAsset(root, _testRoot + "/Reference Only.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            RegisterConsumers(fixture.Previous.Recipe, referenceOnly);
            Assert.That(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(referenceOnly)),
                Does.Contain(fixture.First.Path));
            Dictionary<string, byte[]> before = CaptureFiles();

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Error), preview.ToString());
            Assert.That(preview.Errors, Has.Some.Contains("does not contain"));
            Assert.That(preview.CanPreviewOverrideRepairs, Is.False);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void ReplacementTokenApplyReachesReadyAndPreservesConsumerAndOtherOwners()
        {
            Fixture fixture = CreateFixture();
            StyleReview initial = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(initial.State, Is.EqualTo(StyleReviewState.Ready), initial.ToString());
            SetPrivateField(fixture.First.Token, "_value", Color.green);
            EditorUtility.SetDirty(fixture.First.Token);
            AssetDatabase.SaveAssets();
            Dictionary<string, byte[]> before = CaptureFiles();
            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Stale), preview.ToString());

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);

            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            foreach (KeyValuePair<string, byte[]> pair in before)
            {
                if (!string.Equals(pair.Key.Replace('\\', '/'), fixture.First.Path, StringComparison.Ordinal))
                {
                    Assert.That(File.ReadAllBytes(pair.Key), Is.EqualTo(pair.Value), pair.Key);
                }
            }

            Assert.That(File.ReadAllBytes(fixture.First.Path), Is.Not.EqualTo(before[fixture.First.Path]));
            AssertConsumerValues(fixture);
            Dictionary<string, byte[]> after = CaptureFiles();
            StyleReview repeated = StyleRecipeProcessor.Apply(fixture.Registry, applied);
            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready), repeated.ToString());
            AssertFilesUnchanged(after);
        }

        [Test]
        public void RegisteredConsumerRootOwnerIsValidatedWithoutInventingNestedInstances()
        {
            Fixture fixture = CreateFixture();
            RegisterConsumers(fixture.Previous.Recipe, fixture.First.Prefab);
            Dictionary<string, byte[]> before = CaptureFiles();

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Ready), preview.ToString());
            Assert.That(preview.Changes, Is.Empty);
            AssertFilesUnchanged(before);
        }

        private Fixture CreateFixture()
        {
            Owner previous = CreateOwner("Previous", Color.blue);
            Owner first = CreateOwner("First", Color.red);
            Owner second = CreateOwner("Second", Color.yellow);
            var root = new GameObject("Consumer", typeof(RectTransform));
            GameObject consumer;
            try
            {
                foreach (Owner owner in new[] { first, second })
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(owner.Prefab);
                    instance.transform.SetParent(root.transform, false);
                    var transform = (RectTransform)instance.transform;
                    transform.anchoredPosition = new Vector2(37f, 19f);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
                    Image image = instance.GetComponent<Image>();
                    image.raycastTarget = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                }

                consumer = PrefabUtility.SaveAsPrefabAsset(root, _testRoot + "/Consumer.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            RegisterConsumers(previous.Recipe, consumer);
            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(registry, "_recipes", new[] { previous.Recipe, first.Recipe, second.Recipe });
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return new Fixture(previous, first, second, registry, consumer);
        }

        private Owner CreateOwner(string name, Color color)
        {
            string path = _testRoot + "/" + name + ".prefab";
            var root = new GameObject(name, typeof(RectTransform), typeof(Image));
            GameObject prefab;
            try
            {
                root.GetComponent<Image>().color = color;
                var child = new GameObject("Unmanaged Child", typeof(RectTransform), typeof(Image));
                child.transform.SetParent(root.transform, false);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            ColorToken token = CreateAsset<ColorToken>(name + " Color.asset");
            SetPrivateField(token, "_value", color);
            EditorUtility.SetDirty(token);
            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>(name + " Recipe.asset");
            SetPrivateField(recipe, "_ownerPrefab", prefab);
            SetPrivateField(recipe, "_graphicColors", new[] { CreateBinding(path, null, token) });
            EditorUtility.SetDirty(recipe);
            return new Owner(path, prefab, recipe, token);
        }

        private static PrefabStyleRecipe.GraphicColorBinding CreateBinding(
            string ownerPath,
            string childPath,
            ColorToken token)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ownerPath);
            try
            {
                Transform transform = childPath == null ? root.transform : root.transform.Find(childPath);
                Image image = transform.GetComponent<Image>();
                var target = new PrefabTargetReference();
                target.Configure(
                    GlobalObjectId.GetGlobalObjectIdSlow(image).ToString(),
                    PrefabTargetResolver.GetDisplayPath(root.transform, transform),
                    typeof(Image).FullName,
                    PrefabTargetKind.Graphic);
                var binding = new PrefabStyleRecipe.GraphicColorBinding();
                SetPrivateField(binding, "_target", target);
                SetPrivateField(binding, "_color", token);
                return binding;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RegisterConsumers(PrefabStyleRecipe recipe, params GameObject[] consumers)
        {
            SetPrivateField(recipe, "_consumerPrefabs", consumers);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssets();
        }

        private static void OverrideConsumerColor(GameObject consumer, int instanceIndex, bool child, Color color)
        {
            string path = AssetDatabase.GetAssetPath(consumer);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform instance = root.transform.GetChild(instanceIndex);
                Image image = (child ? instance.GetChild(0) : instance).GetComponent<Image>();
                image.color = color;
                PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssertConsumerValues(Fixture fixture)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(fixture.Consumer));
            try
            {
                Owner[] owners = { fixture.First, fixture.Second };
                for (int index = 0; index < owners.Length; index++)
                {
                    Transform instance = root.transform.GetChild(index);
                    Image image = instance.GetComponent<Image>();
                    Assert.That(image.color, Is.EqualTo(owners[index].Token.Value));
                    Assert.That(image.raycastTarget, Is.False);
                    Assert.That(((RectTransform)instance).anchoredPosition, Is.EqualTo(new Vector2(37f, 19f)));
                    Assert.That(instance.GetChild(0).GetComponent<Image>().color, Is.EqualTo(Color.white));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private Dictionary<string, byte[]> CaptureFiles()
        {
            return Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path.Replace('\\', '/'), File.ReadAllBytes);
        }

        private static void AssertFilesUnchanged(IReadOnlyDictionary<string, byte[]> before)
        {
            foreach (KeyValuePair<string, byte[]> pair in before)
            {
                Assert.That(File.ReadAllBytes(pair.Key), Is.EqualTo(pair.Value), pair.Key);
            }
        }

        private T CreateAsset<T>(string name) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, _testRoot + "/" + name);
            return asset;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private sealed class Owner
        {
            public string Path { get; }
            public GameObject Prefab { get; }
            public PrefabStyleRecipe Recipe { get; }
            public ColorToken Token { get; }

            public Owner(string path, GameObject prefab, PrefabStyleRecipe recipe, ColorToken token)
            {
                Path = path;
                Prefab = prefab;
                Recipe = recipe;
                Token = token;
            }
        }

        private sealed class Fixture
        {
            public Owner Previous { get; }
            public Owner First { get; }
            public Owner Second { get; }
            public StyleRecipeRegistry Registry { get; }
            public GameObject Consumer { get; }

            public Fixture(Owner previous, Owner first, Owner second, StyleRecipeRegistry registry, GameObject consumer)
            {
                Previous = previous;
                First = first;
                Second = second;
                Registry = registry;
                Consumer = consumer;
            }
        }
    }
}
