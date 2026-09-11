using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipeIncrementalTests
    {
        private readonly List<ColorToken> _tokensWithUndo = new();
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            StyleRecipeProcessor.ClearPreviewCache();
            _testRoot = "Assets/__SuperHeroUIIncrementalTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ColorToken token in _tokensWithUndo)
            {
                if (token != null)
                {
                    Undo.ClearUndo(token);
                }
            }

            _tokensWithUndo.Clear();
            StyleRecipeProcessor.ClearPreviewCache();
            if (!string.IsNullOrEmpty(_testRoot))
            {
                AssetDatabase.DeleteAsset(_testRoot);
            }

            _testRoot = null;
            AssetDatabase.Refresh();
        }

        [TestCase(3)]
        [TestCase(100)]
        public void UnchangedPreviewReusesOwnersAndConsumersAndMatchesFullInspection(int ownerCount)
        {
            Fixture fixture = CreateFixture(ownerCount, true);
            IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();
            StyleReview initial = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(initial.State, Is.EqualTo(StyleReviewState.Stale), initial.ToString());
            Assert.That(initial.OwnerPrefabLoadCount, Is.EqualTo(ownerCount));
            Assert.That(initial.ConsumerPrefabLoadCount, Is.EqualTo(1));
            var stopwatch = Stopwatch.StartNew();

            StyleReview cached = StyleRecipeProcessor.Preview(fixture.Registry);

            stopwatch.Stop();
            TestContext.WriteLine(
                $"Unchanged Preview: {ownerCount} owners, "
                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms, "
                + $"{cached.OwnerPrefabLoadCount} owner loads, "
                + $"{cached.ConsumerPrefabLoadCount} consumer loads.");
            Assert.That(cached.OwnerPrefabLoadCount, Is.Zero);
            Assert.That(cached.ConsumerPrefabLoadCount, Is.Zero);
            AssertReviewsEqual(initial, cached);
            AssertMatchesFull(fixture.Registry, cached);
            AssertFilesUnchanged(before);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnsavedTokenEditReloadsOnlyItsOwnerAndAffectedConsumer(bool sharedConsumer)
        {
            Fixture fixture = CreateFixture(3, sharedConsumer);
            StyleRecipeProcessor.Preview(fixture.Registry);
            IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();
            SetColor(fixture.Tokens[1], Color.magenta);

            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(changed.State, Is.EqualTo(StyleReviewState.Stale), changed.ToString());
            Assert.That(changed.OwnerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(changed.ConsumerPrefabLoadCount, Is.EqualTo(1));
            AssertMatchesFull(fixture.Registry, changed);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void AdditionalEditToAlreadyDirtyTokenInvalidatesReviewAndRejectsOldApproval()
        {
            Fixture fixture = CreateFixture();
            StyleRecipeProcessor.Preview(fixture.Registry);
            IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();
            SetColor(fixture.Tokens[1], Color.magenta);
            StyleReview firstEdit = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(EditorUtility.IsDirty(fixture.Tokens[1]), Is.True);
            SetColor(fixture.Tokens[1], Color.cyan);

            StyleReview secondEdit = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(secondEdit.OwnerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(secondEdit.Fingerprint, Is.Not.EqualTo(firstEdit.Fingerprint));
            Assert.That(secondEdit.Changes, Is.Not.EqualTo(firstEdit.Changes));
            AssertMatchesFull(fixture.Registry, secondEdit);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(fixture.Registry, firstEdit));
            StringAssert.Contains("changed after Preview", exception.Message);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void UndoAndRedoRefreshCachedReview()
        {
            Fixture fixture = CreateFixture();
            ColorToken token = fixture.Tokens[1];
            _tokensWithUndo.Add(token);
            StyleReview initial = StyleRecipeProcessor.Preview(fixture.Registry);
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(token, "Change incremental test color");
            SetColor(token, Color.magenta);
            Undo.FlushUndoRecordObjects();
            StyleReview edited = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(edited.Fingerprint, Is.Not.EqualTo(initial.Fingerprint));

            Undo.PerformUndo();
            StyleReview undone = StyleRecipeProcessor.Preview(fixture.Registry);

            AssertReviewsEqual(initial, undone);
            AssertMatchesFull(fixture.Registry, undone);

            Undo.PerformRedo();
            StyleReview redone = StyleRecipeProcessor.Preview(fixture.Registry);

            AssertReviewsEqual(edited, redone);
            AssertMatchesFull(fixture.Registry, redone);
        }

        [Test]
        public void SharedTokenEditInvalidatesEveryDependentOwner()
        {
            Fixture fixture = CreateFixture(3, true, true);
            StyleRecipeProcessor.Preview(fixture.Registry);
            SetColor(fixture.Tokens[0], Color.magenta);

            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(changed.OwnerPrefabLoadCount, Is.EqualTo(3));
            Assert.That(changed.ConsumerPrefabLoadCount, Is.EqualTo(1));
            AssertMatchesFull(fixture.Registry, changed);
        }

        [Test]
        public void SavedOwnerChangeInvalidatesOwnerAndItsConsumer()
        {
            Fixture fixture = CreateFixture();
            StyleReview approved = StyleRecipeProcessor.Preview(fixture.Registry);
            string ownerPath = AssetDatabase.GetAssetPath(fixture.Recipes[1].OwnerPrefab);
            GameObject root = PrefabUtility.LoadPrefabContents(ownerPath);
            try
            {
                root.GetComponent<Image>().color = fixture.Tokens[1].Value;
                Assert.That(PrefabUtility.SaveAsPrefabAsset(root, ownerPath), Is.Not.Null);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(changed.OwnerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(changed.ConsumerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(changed.Changes.Count, Is.EqualTo(2));
            Assert.That(changed.Fingerprint, Is.Not.EqualTo(approved.Fingerprint));
            AssertMatchesFull(fixture.Registry, changed);
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(fixture.Registry, approved));
        }

        [Test]
        public void SavedConsumerOverrideInvalidatesPreviouslyValidInspection()
        {
            Fixture fixture = CreateFixture(3, true);
            StyleReview approved = StyleRecipeProcessor.Preview(fixture.Registry);
            string consumerPath = AssetDatabase.GetAssetPath(fixture.Consumers[0]);
            GameObject root = PrefabUtility.LoadPrefabContents(consumerPath);
            try
            {
                Image image = root.transform.GetChild(2).GetComponent<Image>();
                image.color = Color.black;
                PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(root, consumerPath), Is.Not.Null);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(changed.State, Is.EqualTo(StyleReviewState.Error), changed.ToString());
            Assert.That(changed.OwnerPrefabLoadCount, Is.Zero);
            Assert.That(changed.ConsumerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(
                changed.Errors.Any(error => error.Contains("Owner 002")
                    && error.Contains("overrides a recipe-owned value")),
                Is.True,
                changed.ToString());
            AssertMatchesFull(fixture.Registry, changed);
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(fixture.Registry, approved));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DirtyTrackedPrefabStageInvalidatesCacheAndRecoversAfterDiscard(bool consumerStage)
        {
            Fixture fixture = CreateFixture(3, true);
            HideFlags originalFlags = fixture.Registry.hideFlags;
            // Opening Prefab Mode unloads assets held only on the managed test stack.
            fixture.Registry.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            try
            {
                StyleReview approved = StyleRecipeProcessor.Preview(fixture.Registry);
                IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();
                int registryInstanceId = fixture.Registry.GetInstanceID();
                string stagePath = AssetDatabase.GetAssetPath(
                    consumerStage ? fixture.Consumers[0] : fixture.Recipes[1].OwnerPrefab);
                PrefabStage stage = PrefabStageUtility.OpenPrefab(stagePath);
                try
                {
                    Assert.That(stage, Is.Not.Null);
                    Assert.That(fixture.Registry == null, Is.False, "Keep the cached registry alive.");
                    Assert.That(fixture.Registry.GetInstanceID(), Is.EqualTo(registryInstanceId));
                    Assert.That(EditorSceneManager.MarkSceneDirty(stage.scene), Is.True);
                    Assert.That(stage.scene.isDirty, Is.True);

                    StyleReview dirty = StyleRecipeProcessor.Preview(fixture.Registry);

                    Assert.That(dirty.State, Is.EqualTo(StyleReviewState.Error), dirty.ToString());
                    Assert.That(
                        dirty.Errors.Any(error => error.Contains(stagePath)
                            && error.Contains("unsaved changes in Prefab Mode")),
                        Is.True,
                        dirty.ToString());
                    AssertMatchesFull(fixture.Registry, dirty);
                    StyleReview repeated = StyleRecipeProcessor.Preview(fixture.Registry);
                    AssertReviewsEqual(dirty, repeated);
                    Assert.Throws<InvalidOperationException>(
                        () => StyleRecipeProcessor.Apply(fixture.Registry, approved));
                    AssertFilesUnchanged(before);
                }
                finally
                {
                    if (stage != null)
                    {
                        stage.ClearDirtiness();
                        StageUtility.GoToMainStage();
                    }
                }

                StyleReview recovered = StyleRecipeProcessor.Preview(fixture.Registry);

                AssertReviewsEqual(approved, recovered);
                AssertMatchesFull(fixture.Registry, recovered);
                AssertFilesUnchanged(before);
            }
            finally
            {
                if (fixture.Registry != null)
                {
                    fixture.Registry.hideFlags = originalFlags;
                }
            }
        }

        [Test]
        public void RemovingReorderingAndAddingRecipesMatchesFullInspection()
        {
            Fixture fixture = CreateFixture();
            StyleRecipeProcessor.Preview(fixture.Registry);
            SetRegisteredRecipes(fixture.Registry, fixture.Recipes.Take(2).ToArray());

            StyleReview removed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(removed.Changes.Count, Is.EqualTo(2));
            AssertMatchesFull(fixture.Registry, removed);
            SetRegisteredRecipes(
                fixture.Registry,
                new[] { fixture.Recipes[1], fixture.Recipes[0] });

            StyleReview reordered = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(reordered.Changes, Is.EqualTo(removed.Changes.Reverse().ToArray()));
            AssertMatchesFull(fixture.Registry, reordered);
            SetRegisteredRecipes(
                fixture.Registry,
                new[] { fixture.Recipes[1], fixture.Recipes[0], fixture.Recipes[2] });

            StyleReview added = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(added.Changes.Count, Is.EqualTo(3));
            AssertMatchesFull(fixture.Registry, added);
        }

        [Test]
        public void ReplacingTokenReferenceInvalidatesOwnerWithoutSavingRecipe()
        {
            Fixture fixture = CreateFixture();
            StyleReview initial = StyleRecipeProcessor.Preview(fixture.Registry);
            SetPrivateField(fixture.Recipes[1].GraphicColors[0], "_color", fixture.Tokens[2]);
            EditorUtility.SetDirty(fixture.Recipes[1]);

            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(changed.OwnerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(changed.Fingerprint, Is.Not.EqualTo(initial.Fingerprint));
            Assert.That(changed.Changes, Is.Not.EqualTo(initial.Changes));
            AssertMatchesFull(fixture.Registry, changed);
        }

        [Test]
        public void DeletedTokenInvalidatesPreviouslyValidInspection()
        {
            Fixture fixture = CreateFixture();
            StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(fixture.Tokens[1])), Is.True);

            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(changed.State, Is.EqualTo(StyleReviewState.Error), changed.ToString());
            Assert.That(
                changed.Errors.Any(error => error.Contains("Color is missing")),
                Is.True,
                changed.ToString());
            AssertMatchesFull(fixture.Registry, changed);
        }

        [Test]
        public void CachedReadyReviewRemainsReadyAfterApplyAndSecondApplyDoesNotWrite()
        {
            Fixture fixture = CreateFixture(3, true);
            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);
            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();

            StyleReview cached = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(cached.State, Is.EqualTo(StyleReviewState.Ready), cached.ToString());
            Assert.That(cached.OwnerPrefabLoadCount, Is.Zero);
            Assert.That(cached.ConsumerPrefabLoadCount, Is.Zero);
            AssertMatchesFull(fixture.Registry, cached);

            StyleReview repeated = StyleRecipeProcessor.Apply(fixture.Registry, cached);

            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready), repeated.ToString());
            Assert.That(repeated.Changes, Is.Empty);
            AssertFilesUnchanged(before);
        }

        [TestCase("Owner")]
        [TestCase("Consumer")]
        [TestCase("Recipe")]
        [TestCase("Token")]
        public void MovingTrackedAssetInvalidatesOldApprovalAndMatchesFullInspection(string assetKind)
        {
            Fixture fixture = CreateFixture(3, true);
            StyleReview approved = StyleRecipeProcessor.Preview(fixture.Registry);
            UnityEngine.Object asset = assetKind switch
            {
                "Owner" => fixture.Recipes[1].OwnerPrefab,
                "Consumer" => fixture.Consumers[1],
                "Recipe" => fixture.Recipes[1],
                _ => fixture.Tokens[1],
            };
            string previousPath = AssetDatabase.GetAssetPath(asset);
            string guid = AssetDatabase.AssetPathToGUID(previousPath);
            string movedPath = _testRoot + "/Moved " + Path.GetFileName(previousPath);

            Assert.That(AssetDatabase.MoveAsset(previousPath, movedPath), Is.Empty);
            Assert.That(AssetDatabase.AssetPathToGUID(movedPath), Is.EqualTo(guid));
            IReadOnlyDictionary<string, FileSnapshot> before = CaptureFiles();
            StyleReview moved = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(moved.State, Is.EqualTo(StyleReviewState.Stale), moved.ToString());
            Assert.That(moved.Fingerprint, Is.Not.EqualTo(approved.Fingerprint));
            AssertMatchesFull(fixture.Registry, moved);
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(fixture.Registry, approved));
            AssertFilesUnchanged(before);
        }

        [Test]
        public void ApplyInvalidatesAnotherRegistrysCachedOwnersThroughSavedDependencies()
        {
            Fixture fixture = CreateFixture(3, true);
            StyleRecipeRegistry secondRegistry = CreateAsset<StyleRecipeRegistry>("Second Registry.asset");
            SetRegisteredRecipes(secondRegistry, fixture.Recipes);
            AssetDatabase.SaveAssets();
            StyleReview approved = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview secondBefore = StyleRecipeProcessor.Preview(secondRegistry);
            Assert.That(secondBefore.State, Is.EqualTo(StyleReviewState.Stale), secondBefore.ToString());

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, approved);
            StyleReview secondAfter = StyleRecipeProcessor.Preview(secondRegistry);

            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            Assert.That(secondAfter.State, Is.EqualTo(StyleReviewState.Ready), secondAfter.ToString());
            Assert.That(secondAfter.OwnerPrefabLoadCount, Is.EqualTo(3));
            Assert.That(secondAfter.ConsumerPrefabLoadCount, Is.EqualTo(1));
            AssertMatchesFull(secondRegistry, secondAfter);
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(secondRegistry, secondBefore));
        }

        [TestCase(3)]
        [TestCase(100)]
        public void ApplyingOneChangedTokenVisitsOnlyItsOwner(int ownerCount)
        {
            Fixture fixture = CreateFixture(ownerCount, true);
            StyleReview initial = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview ready = StyleRecipeProcessor.Apply(fixture.Registry, initial);
            Assert.That(ready.State, Is.EqualTo(StyleReviewState.Ready), ready.ToString());
            string firstOwnerPath = AssetDatabase.GetAssetPath(fixture.Recipes[0].OwnerPrefab);
            string lastOwnerPath = AssetDatabase.GetAssetPath(fixture.Recipes[ownerCount - 1].OwnerPrefab);
            var firstBefore = new FileSnapshot(firstOwnerPath);
            var lastBefore = new FileSnapshot(lastOwnerPath);
            SetColor(fixture.Tokens[1], Color.magenta);
            var stopwatch = Stopwatch.StartNew();

            StyleReview changed = StyleRecipeProcessor.Preview(fixture.Registry);

            stopwatch.Stop();
            TestContext.WriteLine(
                $"One-token Preview: {ownerCount} owners, "
                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms, "
                + $"{changed.OwnerPrefabLoadCount} owner loads, "
                + $"{changed.ConsumerPrefabLoadCount} consumer loads.");
            Assert.That(changed.Changes.Count, Is.EqualTo(1));
            Assert.That(changed.OwnerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(changed.ConsumerPrefabLoadCount, Is.EqualTo(1));
            stopwatch.Restart();

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, changed);

            stopwatch.Stop();
            TestContext.WriteLine(
                $"One-token Apply: {ownerCount} owners, "
                + $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms, "
                + $"{applied.AppliedOwnerPrefabCount} owners visited for Apply, "
                + $"{applied.OwnerPrefabLoadCount} final owner loads, "
                + $"{applied.ConsumerPrefabLoadCount} final consumer loads.");
            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            Assert.That(applied.AppliedOwnerPrefabCount, Is.EqualTo(1));
            Assert.That(applied.OwnerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(applied.ConsumerPrefabLoadCount, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(firstOwnerPath), Is.EqualTo(firstBefore.Bytes));
            Assert.That(File.GetLastWriteTimeUtc(firstOwnerPath), Is.EqualTo(firstBefore.LastWriteTimeUtc));
            Assert.That(File.ReadAllBytes(lastOwnerPath), Is.EqualTo(lastBefore.Bytes));
            Assert.That(File.GetLastWriteTimeUtc(lastOwnerPath), Is.EqualTo(lastBefore.LastWriteTimeUtc));
            GameObject root = PrefabUtility.LoadPrefabContents(
                AssetDatabase.GetAssetPath(fixture.Recipes[1].OwnerPrefab));
            try
            {
                Assert.That(root.GetComponent<Image>().color, Is.EqualTo(Color.magenta));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssertMatchesFull(fixture.Registry, applied);
        }

        private Fixture CreateFixture(
            int ownerCount = 3,
            bool sharedConsumer = false,
            bool sharedToken = false)
        {
            var owners = new GameObject[ownerCount];
            var recipes = new PrefabStyleRecipe[ownerCount];
            var tokens = new ColorToken[ownerCount];
            var consumers = new GameObject[ownerCount];
            for (int index = 0; index < ownerCount; index++)
            {
                if (sharedToken && index > 0)
                {
                    tokens[index] = tokens[0];
                }
                else
                {
                    tokens[index] = CreateAsset<ColorToken>($"Color {index:D3}.asset");
                    SetColor(tokens[index], new Color((index + 1f) / (ownerCount + 1f), 0.25f, 0.5f, 1f));
                }

                string ownerName = $"Owner {index:D3}";
                string ownerPath = _testRoot + "/" + ownerName + ".prefab";
                var root = new GameObject(ownerName, typeof(RectTransform), typeof(Image));
                try
                {
                    root.GetComponent<Image>().color = Color.white;
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
                SetPrivateField(binding, "_color", tokens[index]);
                recipes[index] = CreateAsset<PrefabStyleRecipe>(ownerName + " Recipe.asset");
                SetPrivateField(recipes[index], "_ownerPrefab", owners[index]);
                SetPrivateField(recipes[index], "_graphicColors", new[] { binding });
            }

            GameObject commonConsumer = sharedConsumer
                ? CreateConsumer("Shared Consumer", owners)
                : null;
            for (int index = 0; index < ownerCount; index++)
            {
                consumers[index] = sharedConsumer
                    ? commonConsumer
                    : CreateConsumer($"Consumer {index:D3}", new[] { owners[index] });
                SetPrivateField(recipes[index], "_consumerPrefabs", new[] { consumers[index] });
                EditorUtility.SetDirty(recipes[index]);
            }

            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetRegisteredRecipes(registry, recipes);
            AssetDatabase.SaveAssets();
            return new Fixture(registry, recipes, tokens, consumers);
        }

        private GameObject CreateConsumer(string name, GameObject[] owners)
        {
            var root = new GameObject(name, typeof(RectTransform));
            try
            {
                foreach (GameObject owner in owners)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(owner);
                    instance.transform.SetParent(root.transform, false);
                }

                return PrefabUtility.SaveAsPrefabAsset(root, _testRoot + "/" + name + ".prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertMatchesFull(StyleRecipeRegistry registry, StyleReview incremental)
        {
            StyleReview full = StyleRecipeProcessor.PreviewFull(registry);
            AssertReviewsEqual(full, incremental);
        }

        private static void AssertReviewsEqual(StyleReview expected, StyleReview actual)
        {
            Assert.That(actual.State, Is.EqualTo(expected.State), actual.ToString());
            Assert.That(actual.Assets, Is.EqualTo(expected.Assets), "Assets differ from full inspection.");
            Assert.That(actual.Changes, Is.EqualTo(expected.Changes), "Changes differ from full inspection.");
            Assert.That(actual.Errors, Is.EqualTo(expected.Errors), "Errors differ from full inspection.");
            Assert.That(actual.Fingerprint, Is.EqualTo(expected.Fingerprint));
        }

        private static void SetColor(ColorToken token, Color color)
        {
            SetPrivateField(token, "_value", color);
            EditorUtility.SetDirty(token);
        }

        private static void SetRegisteredRecipes(StyleRecipeRegistry registry, PrefabStyleRecipe[] recipes)
        {
            SetPrivateField(registry, "_recipes", recipes);
            EditorUtility.SetDirty(registry);
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
            public ColorToken[] Tokens { get; }
            public GameObject[] Consumers { get; }

            public Fixture(
                StyleRecipeRegistry registry,
                PrefabStyleRecipe[] recipes,
                ColorToken[] tokens,
                GameObject[] consumers)
            {
                Registry = registry;
                Recipes = recipes;
                Tokens = tokens;
                Consumers = consumers;
            }
        }
    }
}
