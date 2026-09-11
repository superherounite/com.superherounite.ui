using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipePerformanceComparisonTests
    {
        private const int BindingsPerOwner = 4;
        private const string BaselineRevision = "7ede1ed6229c7b03b4d3adfddc42a283d6ef63b0";
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            StyleRecipeProcessor.ClearPreviewCache();
            _testRoot = "Assets/__SuperHeroUIComparison_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            StyleRecipeProcessor.ClearPreviewCache();
            if (!string.IsNullOrEmpty(_testRoot))
            {
                AssetDatabase.DeleteAsset(_testRoot);
            }

            _testRoot = null;
            AssetDatabase.Refresh();
        }

        [TestCase(100)]
        [TestCase(200)]
        [TestCase(300)]
        [Timeout(1200000)]
        [Category("PerformanceComparison")]
        public void CompareLegacyAndCurrentOnIdenticalAssets(int ownerCount)
        {
            Fixture fixture = CreateFixture(ownerCount);
            Dictionary<string, byte[]> originalOwnerFiles = fixture.OwnerPaths
                .ToDictionary(path => path, File.ReadAllBytes);
            byte[] consumerBytes = File.ReadAllBytes(fixture.ConsumerPath);
            TestContext.WriteLine(
                $"WORKLOAD: Unity {Application.unityVersion}, {ownerCount} owners, "
                + $"{BindingsPerOwner} ImageStyle bindings per owner, "
                + "one unique ColorToken and ImageStyle per owner, "
                + $"one shared consumer containing {ownerCount} nested owners; "
                + "three steady-state samples, all measured on identical GUIDs and assets.");

            RunResult baseline = Run(fixture, false);
            ResetFixture(fixture, originalOwnerFiles);
            RunResult current = Run(fixture, true);

            AssertPublicReviewsEqual(baseline.InitialPreview, current.InitialPreview);
            AssertPublicReviewsEqual(baseline.InitialApplied, current.InitialApplied);
            for (int index = 0; index < 3; index++)
            {
                AssertPublicReviewsEqual(baseline.SparsePreviews[index], current.SparsePreviews[index]);
                AssertPublicReviewsEqual(baseline.SparseApplied[index], current.SparseApplied[index]);
            }

            Assert.That(File.ReadAllBytes(fixture.ConsumerPath), Is.EqualTo(consumerBytes));
            var report = new ComparisonReport
            {
                UnityVersion = Application.unityVersion,
                BaselineRevision = BaselineRevision,
                OwnerCount = ownerCount,
                BindingsPerOwner = BindingsPerOwner,
                Legacy = baseline.Measurements,
                Current = current.Measurements,
                DependencyFingerprintMilliseconds = MeasureDependencyFingerprint(fixture),
                InspectionSnapshotMilliseconds = MeasureInspectionSnapshot(fixture),
            };
            report.DependencyFingerprintMedianMilliseconds = Median(report.DependencyFingerprintMilliseconds);
            report.InspectionSnapshotMedianMilliseconds = Median(report.InspectionSnapshotMilliseconds);
            PrintComparison(report);
            Directory.CreateDirectory("Results");
            string resultPath = $"Results/legacy-vs-current-{ownerCount}-owners.json";
            File.WriteAllText(resultPath, JsonUtility.ToJson(report, true));
            TestContext.WriteLine("RESULT_JSON: " + Path.GetFullPath(resultPath));
        }

        private RunResult Run(Fixture fixture, bool current)
        {
            StyleRecipeProcessor.ClearPreviewCache();
            var result = new RunResult(current ? "current" : "legacy baseline");
            string label = result.Measurements.Processor;
            IReadOnlyDictionary<string, FileSnapshot> beforePreview = CaptureFiles();
            var stopwatch = Stopwatch.StartNew();
            result.InitialPreview = Preview(fixture.Registry, current);
            stopwatch.Stop();
            result.Measurements.InitialPreviewMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            Assert.That(result.InitialPreview.State, Is.EqualTo(StyleReviewState.Stale), result.InitialPreview.ToString());
            Assert.That(result.InitialPreview.Errors, Is.Empty);
            Assert.That(result.InitialPreview.Changes.Count, Is.EqualTo(fixture.OwnerPaths.Length * BindingsPerOwner));
            AssertFilesUnchanged(beforePreview);

            stopwatch.Restart();
            result.InitialApplied = Apply(fixture.Registry, result.InitialPreview, current);
            stopwatch.Stop();
            result.Measurements.InitialApplyMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            AssertReady(result.InitialApplied);
            TestContext.WriteLine(
                $"{label}: initial Preview {result.Measurements.InitialPreviewMilliseconds:F2} ms, "
                + $"initial Apply {result.Measurements.InitialApplyMilliseconds:F2} ms.");

            IReadOnlyDictionary<string, FileSnapshot> readyFiles = CaptureFiles();
            for (int index = 0; index < 3; index++)
            {
                stopwatch.Restart();
                StyleReview warm = Preview(fixture.Registry, current);
                stopwatch.Stop();
                result.Measurements.WarmPreviewMilliseconds[index] = stopwatch.Elapsed.TotalMilliseconds;
                AssertReady(warm);
                if (current)
                {
                    Assert.That(warm.OwnerPrefabLoadCount, Is.Zero);
                    Assert.That(warm.ConsumerPrefabLoadCount, Is.Zero);
                }
            }

            AssertFilesUnchanged(readyFiles);
            Color[] changedColors = { Color.magenta, Color.cyan, Color.yellow };
            for (int index = 0; index < changedColors.Length; index++)
            {
                IReadOnlyDictionary<string, FileSnapshot> beforeChange = CaptureFiles();
                SetColor(fixture.Tokens[1], changedColors[index]);
                stopwatch.Restart();
                StyleReview sparse = Preview(fixture.Registry, current);
                stopwatch.Stop();
                result.SparsePreviews[index] = sparse;
                result.Measurements.SparsePreviewMilliseconds[index] = stopwatch.Elapsed.TotalMilliseconds;
                Assert.That(sparse.State, Is.EqualTo(StyleReviewState.Stale), sparse.ToString());
                Assert.That(sparse.Errors, Is.Empty);
                Assert.That(sparse.Changes.Count, Is.EqualTo(BindingsPerOwner));
                AssertFilesUnchanged(beforeChange);
                if (current)
                {
                    Assert.That(sparse.OwnerPrefabLoadCount, Is.EqualTo(1));
                    Assert.That(sparse.ConsumerPrefabLoadCount, Is.EqualTo(1));
                }

                stopwatch.Restart();
                StyleReview applied = Apply(fixture.Registry, sparse, current);
                stopwatch.Stop();
                result.SparseApplied[index] = applied;
                result.Measurements.SparseApplyMilliseconds[index] = stopwatch.Elapsed.TotalMilliseconds;
                AssertReady(applied);
                AssertFilesUnchanged(beforeChange, fixture.OwnerPaths[1]);
                Assert.That(File.ReadAllBytes(fixture.OwnerPaths[1]), Is.Not.EqualTo(beforeChange[fixture.OwnerPaths[1]].Bytes));
                if (current)
                {
                    Assert.That(applied.AppliedOwnerPrefabCount, Is.EqualTo(1));
                    Assert.That(applied.OwnerPrefabLoadCount, Is.EqualTo(1));
                    Assert.That(applied.ConsumerPrefabLoadCount, Is.EqualTo(1));
                }

                TestContext.WriteLine(
                    $"{label} sparse sample {index + 1}: Preview "
                    + $"{result.Measurements.SparsePreviewMilliseconds[index]:F2} ms, Apply "
                    + $"{result.Measurements.SparseApplyMilliseconds[index]:F2} ms."
                    + (current ? " Loads: Preview 1 owner / 1 consumer, Apply 1 owner, final 1 / 1." : string.Empty));
            }

            AssertBakedValues(fixture);
            IReadOnlyDictionary<string, FileSnapshot> finalFiles = CaptureFiles();
            AssertReady(Apply(fixture.Registry, result.SparseApplied[2], current));
            AssertFilesUnchanged(finalFiles);
            result.Measurements.WarmPreviewMedianMilliseconds = Median(result.Measurements.WarmPreviewMilliseconds);
            result.Measurements.SparsePreviewMedianMilliseconds = Median(result.Measurements.SparsePreviewMilliseconds);
            result.Measurements.SparseApplyMedianMilliseconds = Median(result.Measurements.SparseApplyMilliseconds);
            return result;
        }

        private static StyleReview Preview(StyleRecipeRegistry registry, bool current)
        {
            return current
                ? StyleRecipeProcessor.Preview(registry)
                : LegacyStyleRecipeProcessor.Preview(registry);
        }

        private static StyleReview Apply(StyleRecipeRegistry registry, StyleReview review, bool current)
        {
            return current
                ? StyleRecipeProcessor.Apply(registry, review)
                : LegacyStyleRecipeProcessor.Apply(registry, review);
        }

        private static void AssertReady(StyleReview review)
        {
            Assert.That(review.State, Is.EqualTo(StyleReviewState.Ready), review.ToString());
            Assert.That(review.Errors, Is.Empty);
            Assert.That(review.Changes, Is.Empty);
        }

        private static void AssertPublicReviewsEqual(StyleReview expected, StyleReview actual)
        {
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Assets, Is.EqualTo(expected.Assets));
            Assert.That(actual.Errors, Is.EqualTo(expected.Errors));
            Assert.That(actual.Changes, Is.EqualTo(expected.Changes));
        }

        private static double[] MeasureDependencyFingerprint(Fixture fixture)
        {
            var values = new double[3];
            for (int index = 0; index < values.Length; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                string fingerprint = StyleRecipeProcessor.GetDependencyFingerprint(fixture.Registry);
                stopwatch.Stop();
                values[index] = stopwatch.Elapsed.TotalMilliseconds;
                GC.KeepAlive(fingerprint);
            }

            return values;
        }

        private static double[] MeasureInspectionSnapshot(Fixture fixture)
        {
            var values = new double[3];
            for (int index = 0; index < values.Length; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                var snapshot = new StyleInspectionSnapshot(fixture.Recipes);
                foreach (PrefabStyleRecipe recipe in fixture.Recipes)
                {
                    snapshot.GetOwnerKey(recipe);
                    snapshot.GetConsumerKey(recipe);
                }

                stopwatch.Stop();
                values[index] = stopwatch.Elapsed.TotalMilliseconds;
                GC.KeepAlive(snapshot);
            }

            return values;
        }

        private static void PrintComparison(ComparisonReport report)
        {
            TestContext.WriteLine("MEDIANS (3 samples, milliseconds):");
            PrintMetric("Ready warm Preview", report.Legacy.WarmPreviewMedianMilliseconds, report.Current.WarmPreviewMedianMilliseconds);
            PrintMetric("One-token Preview", report.Legacy.SparsePreviewMedianMilliseconds, report.Current.SparsePreviewMedianMilliseconds);
            PrintMetric("One-token Apply", report.Legacy.SparseApplyMedianMilliseconds, report.Current.SparseApplyMedianMilliseconds);
            TestContext.WriteLine(
                $"Current dependency fingerprint median: {report.DependencyFingerprintMedianMilliseconds:F2} ms; "
                + $"snapshot/all keys median: {report.InspectionSnapshotMedianMilliseconds:F2} ms.");
        }

        private static void PrintMetric(string label, double baseline, double current)
        {
            TestContext.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: legacy {1:F2}, current {2:F2}, ratio {3:F2}x.",
                label,
                baseline,
                current,
                baseline / current));
        }

        private static double Median(double[] values)
        {
            return values.OrderBy(value => value).ElementAt(values.Length / 2);
        }

        private Fixture CreateFixture(int ownerCount)
        {
            var owners = new GameObject[ownerCount];
            var ownerPaths = new string[ownerCount];
            var recipes = new PrefabStyleRecipe[ownerCount];
            var tokens = new ColorToken[ownerCount];
            var initialColors = new Color[ownerCount];
            for (int index = 0; index < ownerCount; index++)
            {
                initialColors[index] = new Color((index + 1f) / (ownerCount + 1f), 0.25f, 0.5f, 1f);
                tokens[index] = CreateAsset<ColorToken>($"Color {index:D3}.asset");
                SetColor(tokens[index], initialColors[index]);
                ImageStyle style = CreateAsset<ImageStyle>($"Image Style {index:D3}.asset");
                SetPrivateField(style, "_tint", tokens[index]);
                EditorUtility.SetDirty(style);
                string ownerName = $"Owner {index:D3}";
                ownerPaths[index] = _testRoot + "/" + ownerName + ".prefab";
                var root = new GameObject(ownerName, typeof(RectTransform));
                try
                {
                    for (int bindingIndex = 0; bindingIndex < BindingsPerOwner; bindingIndex++)
                    {
                        var child = new GameObject($"Image {bindingIndex:D2}", typeof(RectTransform), typeof(Image));
                        child.transform.SetParent(root.transform, false);
                        ((RectTransform)child.transform).anchoredPosition = new Vector2(bindingIndex * 20f, 12f);
                        Image image = child.GetComponent<Image>();
                        image.color = Color.white;
                        image.raycastTarget = false;
                    }

                    owners[index] = PrefabUtility.SaveAsPrefabAsset(root, ownerPaths[index]);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                var bindings = new PrefabStyleRecipe.ImageBinding[BindingsPerOwner];
                GameObject loaded = PrefabUtility.LoadPrefabContents(ownerPaths[index]);
                try
                {
                    for (int bindingIndex = 0; bindingIndex < BindingsPerOwner; bindingIndex++)
                    {
                        Image image = loaded.transform.GetChild(bindingIndex).GetComponent<Image>();
                        var target = new PrefabTargetReference();
                        target.Configure(
                            GlobalObjectId.GetGlobalObjectIdSlow(image).ToString(),
                            PrefabTargetResolver.GetDisplayPath(loaded.transform, image.transform),
                            typeof(Image).FullName,
                            PrefabTargetKind.Image);
                        var binding = new PrefabStyleRecipe.ImageBinding();
                        SetPrivateField(binding, "_target", target);
                        SetPrivateField(binding, "_style", style);
                        bindings[bindingIndex] = binding;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(loaded);
                }

                recipes[index] = CreateAsset<PrefabStyleRecipe>(ownerName + " Recipe.asset");
                SetPrivateField(recipes[index], "_ownerPrefab", owners[index]);
                SetPrivateField(recipes[index], "_images", bindings);
            }

            string consumerPath = _testRoot + "/Shared Consumer.prefab";
            var consumerRoot = new GameObject("Shared Consumer", typeof(RectTransform));
            GameObject consumer;
            try
            {
                foreach (GameObject owner in owners)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(owner);
                    instance.transform.SetParent(consumerRoot.transform, false);
                }

                consumer = PrefabUtility.SaveAsPrefabAsset(consumerRoot, consumerPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(consumerRoot);
            }

            foreach (PrefabStyleRecipe recipe in recipes)
            {
                SetPrivateField(recipe, "_consumerPrefabs", new[] { consumer });
                EditorUtility.SetDirty(recipe);
            }

            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(registry, "_recipes", recipes);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return new Fixture(registry, recipes, tokens, initialColors, ownerPaths, consumerPath);
        }

        private static void ResetFixture(Fixture fixture, IReadOnlyDictionary<string, byte[]> originalOwnerFiles)
        {
            for (int index = 0; index < fixture.Tokens.Length; index++)
            {
                SetColor(fixture.Tokens[index], fixture.InitialColors[index]);
            }

            AssetDatabase.SaveAssets();
            foreach (KeyValuePair<string, byte[]> pair in originalOwnerFiles)
            {
                File.WriteAllBytes(pair.Key, pair.Value);
                AssetDatabase.ImportAsset(pair.Key, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            StyleRecipeProcessor.ClearPreviewCache();
        }

        private static void AssertBakedValues(Fixture fixture)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(fixture.ConsumerPath);
            try
            {
                Assert.That(root.transform.childCount, Is.EqualTo(fixture.OwnerPaths.Length));
                for (int index = 0; index < fixture.OwnerPaths.Length; index++)
                {
                    Image[] images = root.transform.GetChild(index).GetComponentsInChildren<Image>(true);
                    Assert.That(images.Length, Is.EqualTo(BindingsPerOwner));
                    for (int imageIndex = 0; imageIndex < images.Length; imageIndex++)
                    {
                        Image image = images[imageIndex];
                        Assert.That(image.color, Is.EqualTo(fixture.Tokens[index].Value));
                        Assert.That(image.raycastTarget, Is.False);
                        Assert.That(
                            ((RectTransform)image.transform).anchoredPosition,
                            Is.EqualTo(new Vector2(imageIndex * 20f, 12f)));
                    }
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
                .ToDictionary(path => path.Replace('\\', '/'), path => new FileSnapshot(path));
        }

        private void AssertFilesUnchanged(IReadOnlyDictionary<string, FileSnapshot> before, string allowedChange = null)
        {
            IReadOnlyDictionary<string, FileSnapshot> after = CaptureFiles();
            Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
            foreach (KeyValuePair<string, FileSnapshot> pair in before)
            {
                if (string.Equals(pair.Key, allowedChange, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(after[pair.Key].Bytes, Is.EqualTo(pair.Value.Bytes), pair.Key);
                Assert.That(after[pair.Key].LastWriteTimeUtc, Is.EqualTo(pair.Value.LastWriteTimeUtc), pair.Key);
            }
        }

        private T CreateAsset<T>(string fileName)
            where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, _testRoot + "/" + fileName);
            return asset;
        }

        private static void SetColor(ColorToken token, Color color)
        {
            SetPrivateField(token, "_value", color);
            EditorUtility.SetDirty(token);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, target.GetType().Name + "." + name);
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
            public Color[] InitialColors { get; }
            public string[] OwnerPaths { get; }
            public string ConsumerPath { get; }

            public Fixture(StyleRecipeRegistry registry, PrefabStyleRecipe[] recipes, ColorToken[] tokens,
                Color[] initialColors, string[] ownerPaths, string consumerPath)
            {
                Registry = registry;
                Recipes = recipes;
                Tokens = tokens;
                InitialColors = initialColors;
                OwnerPaths = ownerPaths;
                ConsumerPath = consumerPath;
            }
        }

        private sealed class RunResult
        {
            public Measurements Measurements { get; }
            public StyleReview InitialPreview { get; set; }
            public StyleReview InitialApplied { get; set; }
            public StyleReview[] SparsePreviews { get; } = new StyleReview[3];
            public StyleReview[] SparseApplied { get; } = new StyleReview[3];

            public RunResult(string processor)
            {
                Measurements = new Measurements { Processor = processor };
            }
        }

        [Serializable]
        private sealed class Measurements
        {
            public string Processor;
            public double InitialPreviewMilliseconds;
            public double InitialApplyMilliseconds;
            public double[] WarmPreviewMilliseconds = new double[3];
            public double[] SparsePreviewMilliseconds = new double[3];
            public double[] SparseApplyMilliseconds = new double[3];
            public double WarmPreviewMedianMilliseconds;
            public double SparsePreviewMedianMilliseconds;
            public double SparseApplyMedianMilliseconds;
        }

        [Serializable]
        private sealed class ComparisonReport
        {
            public string UnityVersion;
            public string BaselineRevision;
            public int OwnerCount;
            public int BindingsPerOwner;
            public Measurements Legacy;
            public Measurements Current;
            public double[] DependencyFingerprintMilliseconds;
            public double[] InspectionSnapshotMilliseconds;
            public double DependencyFingerprintMedianMilliseconds;
            public double InspectionSnapshotMedianMilliseconds;
        }
    }
}
