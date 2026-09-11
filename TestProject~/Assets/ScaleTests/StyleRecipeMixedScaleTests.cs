using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using TMPro;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    [Category("Scale")]
    public sealed class StyleRecipeMixedScaleTests
    {
        private const int ConsumerCount = 4;
        private const int ChangedOwnerIndex = 18;
        private const string RuntimeAssembly = "SuperHeroUnite.UI.ScaleTests.Runtime";
        private string _testRoot;
        private string _testDirectory;

        [SetUp]
        public void SetUp()
        {
            string folder = "__SuperHeroUIMixedScaleTests_" + Guid.NewGuid().ToString("N");
            _testRoot = "Assets/" + folder;
            _testDirectory = Path.Combine(Application.dataPath, folder);
            AssetDatabase.CreateFolder("Assets", folder);
            StyleRecipeProcessor.ClearPreviewCache();
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
            _testDirectory = null;
            AssetDatabase.Refresh();
        }

        [TestCase(300)]
        [TestCase(1000)]
        [Timeout(600000)]
        public void MixedWorkloadReusesSafeOwnersAndAppliesOnlyChangedDependencyClosure(int ownerCount)
        {
            var setupTime = Stopwatch.StartNew();
            Fixture fixture = CreateFixture(ownerCount);
            setupTime.Stop();
            int callbackOwners = ownerCount / 100;
            HashSet<string> fallbackConsumers = GetFallbackConsumerPaths(fixture.Recipes);
            Dictionary<string, FileSnapshot> before = CaptureFiles();
            TestContext.WriteLine(
                $"Fixture: {ownerCount} owners, {ownerCount / 10} TMP owners, "
                + $"{ownerCount / 10} Variants, {ownerCount / 10} runtime-only scripts, "
                + $"{callbackOwners} ExecuteAlways scripts, {ConsumerCount} shared consumers, "
                + $"{setupTime.Elapsed.TotalMilliseconds:F1} ms setup.");

            StyleReview initial = Measure("Cold Preview", ownerCount, () => StyleRecipeProcessor.Preview(fixture.Registry));
            Assert.That(initial.State, Is.EqualTo(StyleReviewState.Ready), initial.ToString());
            Assert.That(initial.OwnerPrefabLoadCount, Is.EqualTo(ownerCount));
            AssertMatchesFull(fixture.Registry, initial, ownerCount);
            AssertFilesChangedOnly(before);

            StyleReview warm = Measure("Warm Preview", ownerCount, () => StyleRecipeProcessor.Preview(fixture.Registry));
            Assert.That(warm.State, Is.EqualTo(StyleReviewState.Ready), warm.ToString());
            Assert.That(warm.OwnerPrefabLoadCount, Is.EqualTo(callbackOwners));
            Assert.That(warm.ConsumerPrefabLoadCount, Is.EqualTo(fallbackConsumers.Count));
            AssertReviewsEqual(initial, warm);

            SetPrivateField(fixture.Tokens[ChangedOwnerIndex], "_value", Color.magenta);
            StyleReview changed = Measure("Sparse Preview", ownerCount, () => StyleRecipeProcessor.Preview(fixture.Registry));
            Assert.That(changed.State, Is.EqualTo(StyleReviewState.Stale), changed.ToString());
            Assert.That(changed.OwnerPrefabLoadCount, Is.EqualTo(callbackOwners + 1));
            Assert.That(changed.ChangedOwnerPaths, Is.EqualTo(new[] { OwnerPath(ChangedOwnerIndex) }));
            fallbackConsumers.UnionWith(fixture.Recipes[ChangedOwnerIndex].ConsumerPrefabs.Select(AssetDatabase.GetAssetPath));
            Assert.That(changed.ConsumerPrefabLoadCount, Is.EqualTo(fallbackConsumers.Count));
            AssertMatchesFull(fixture.Registry, changed, ownerCount);
            AssertFilesChangedOnly(before);

            StyleReview applied = Measure("Sparse Apply", ownerCount, () => StyleRecipeProcessor.Apply(fixture.Registry, changed));
            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            Assert.That(applied.AppliedOwnerPrefabCount, Is.EqualTo(2));
            Assert.That(applied.OwnerPrefabLoadCount, Is.EqualTo(callbackOwners + 2));
            AssertMatchesFull(fixture.Registry, applied, ownerCount);
            AssertFilesChangedOnly(before, OwnerPath(ChangedOwnerIndex), OwnerPath(ChangedOwnerIndex + 1));
            AssertUnmanagedValues(fixture, ChangedOwnerIndex, Color.magenta);
            AssertUnmanagedValues(fixture, ChangedOwnerIndex + 1, Color.white);
            AssertUnmanagedValues(fixture, 1, Color.white);
            AssertUnmanagedValues(fixture, 3, Color.white);

            Dictionary<string, FileSnapshot> after = CaptureFiles();
            StyleReview repeated = Measure("Repeated Apply", ownerCount, () => StyleRecipeProcessor.Apply(fixture.Registry, applied));
            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready), repeated.ToString());
            Assert.That(repeated.AppliedOwnerPrefabCount, Is.Zero);
            Assert.That(repeated.OwnerPrefabLoadCount, Is.EqualTo(callbackOwners));
            AssertReviewsEqual(applied, repeated);
            AssertFilesChangedOnly(after);
        }

        private Fixture CreateFixture(int ownerCount)
        {
            Type runtimeType = Type.GetType(
                "SuperHeroUnite.UI.ScaleTests.ScaleRuntimeOnlyBehaviour, " + RuntimeAssembly, true);
            Type callbackType = Type.GetType(
                "SuperHeroUnite.UI.ScaleTests.ScaleEditorCallbackBehaviour, " + RuntimeAssembly, true);
            var owners = new GameObject[ownerCount];
            var imageTargets = new PrefabTargetReference[ownerCount];
            var textTargets = new PrefabTargetReference[ownerCount];
            TMP_FontAsset font = null;
            for (int index = 0; index < ownerCount; index++)
            {
                GameObject root = IsVariant(index)
                    ? (GameObject)PrefabUtility.InstantiatePrefab(owners[index - 1])
                    : new GameObject(OwnerName(index), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                try
                {
                    root.name = OwnerName(index);
                    if (!IsVariant(index))
                    {
                        Image image = root.GetComponent<Image>();
                        image.color = Color.white;
                        image.raycastTarget = false;
                        image.preserveAspect = true;
                        var rect = (RectTransform)root.transform;
                        rect.anchoredPosition = ExpectedPosition(index);
                        rect.sizeDelta = new Vector2(128f, 32f);
                    }

                    if (index % 100 == 0)
                    {
                        root.AddComponent(callbackType);
                    }
                    else if (index % 10 == 3)
                    {
                        root.AddComponent(runtimeType);
                    }

                    if (index % 10 == 1)
                    {
                        var child = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                        child.transform.SetParent(root.transform, false);
                        var text = child.GetComponent<TextMeshProUGUI>();
                        font ??= text.font;
                        Assert.That(font, Is.Not.Null, "Import TMP Essential Resources into the validation project.");
                        InitializeText(text, font, index);
                    }

                    owners[index] = PrefabUtility.SaveAsPrefabAsset(root, OwnerPath(index));
                    Assert.That(owners[index], Is.Not.Null);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }

                GameObject loaded = PrefabUtility.LoadPrefabContents(OwnerPath(index));
                try
                {
                    imageTargets[index] = Capture(loaded, loaded.GetComponent<Image>(), PrefabTargetKind.Image);
                    TMP_Text text = loaded.GetComponentInChildren<TMP_Text>(true);
                    if (text != null)
                    {
                        textTargets[index] = Capture(loaded, text, PrefabTargetKind.Text);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(loaded);
                }
            }

            var tokens = new ColorToken[ownerCount];
            var recipes = new PrefabStyleRecipe[ownerCount];
            StyleRecipeRegistry registry;
            AssetDatabase.StartAssetEditing();
            try
            {
                TextStyle textStyle = CreateTextStyle(font);
                for (int index = 0; index < ownerCount; index++)
                {
                    tokens[index] = CreateAsset<ColorToken>(OwnerName(index) + " Token.asset");
                    ImageStyle imageStyle = CreateAsset<ImageStyle>(OwnerName(index) + " Style.asset");
                    SetPrivateField(imageStyle, "_tint", tokens[index]);
                    var imageBinding = new PrefabStyleRecipe.ImageBinding();
                    SetPrivateField(imageBinding, "_target", imageTargets[index]);
                    SetPrivateField(imageBinding, "_style", imageStyle);
                    PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>(OwnerName(index) + " Recipe.asset");
                    SetPrivateField(recipe, "_ownerPrefab", owners[index]);
                    SetPrivateField(recipe, "_images", new[] { imageBinding });
                    if (IsVariant(index))
                    {
                        SetPrivateField(recipe, "_baseRecipe", recipes[index - 1]);
                    }

                    if (textTargets[index] != null)
                    {
                        var textBinding = new PrefabStyleRecipe.TextBinding();
                        SetPrivateField(textBinding, "_target", textTargets[index]);
                        SetPrivateField(textBinding, "_style", textStyle);
                        SetPrivateField(recipe, "_texts", new[] { textBinding });
                    }

                    recipes[index] = recipe;
                }

                registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
                SetPrivateField(registry, "_recipes", recipes.Reverse().ToArray());
                AssetDatabase.SaveAssets();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            GameObject[] consumers = CreateConsumers(owners);
            for (int index = 0; index < ownerCount; index++)
            {
                var registered = new List<GameObject> { consumers[index % ConsumerCount] };
                if (index % 10 == 8)
                {
                    registered.Add(owners[index + 1]);
                    registered.Add(consumers[(index + 1) % ConsumerCount]);
                }

                SetPrivateField(recipes[index], "_consumerPrefabs", registered.ToArray());
            }

            AssetDatabase.SaveAssets();
            return new Fixture { Registry = registry, Recipes = recipes, Tokens = tokens };
        }

        private GameObject[] CreateConsumers(IReadOnlyList<GameObject> owners)
        {
            var consumers = new GameObject[ConsumerCount];
            for (int group = 0; group < ConsumerCount; group++)
            {
                var root = new GameObject("Consumer " + group, typeof(RectTransform));
                try
                {
                    for (int index = group; index < owners.Count; index += ConsumerCount)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(owners[index]);
                        instance.transform.SetParent(root.transform, false);
                    }

                    consumers[group] = PrefabUtility.SaveAsPrefabAsset(root, _testRoot + $"/Consumer {group}.prefab");
                    Assert.That(consumers[group], Is.Not.Null);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            return consumers;
        }

        private TextStyle CreateTextStyle(TMP_FontAsset font)
        {
            ColorToken token = CreateAsset<ColorToken>("Shared Text Token.asset");
            TextStyle style = CreateAsset<TextStyle>("Shared Text Style.asset");
            SetPrivateField(style, "_font", font);
            SetPrivateField(style, "_color", token);
            SetPrivateField(style, "_fontSize", 18f);
            return style;
        }

        private static void InitializeText(TMP_Text text, TMP_FontAsset font, int index)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.text = "Unmanaged label " + index;
            text.raycastTarget = false;
            var serialized = new SerializedObject(text);
            serialized.FindProperty("m_fontColor").colorValue = Color.white;
            serialized.FindProperty("m_fontSize").floatValue = 18f;
            serialized.FindProperty("m_fontSizeBase").floatValue = 18f;
            serialized.FindProperty("m_fontStyle").intValue = (int)FontStyles.Normal;
            serialized.FindProperty("m_enableAutoSizing").boolValue = false;
            serialized.FindProperty("m_fontSizeMin").floatValue = 8f;
            serialized.FindProperty("m_fontSizeMax").floatValue = 40f;
            serialized.FindProperty("m_characterSpacing").floatValue = 0f;
            serialized.FindProperty("m_wordSpacing").floatValue = 0f;
            serialized.FindProperty("m_lineSpacing").floatValue = 0f;
            serialized.FindProperty("m_paragraphSpacing").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static HashSet<string> GetFallbackConsumerPaths(IEnumerable<PrefabStyleRecipe> recipes)
        {
            var reusablePrefabs = new Dictionary<GameObject, bool>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PrefabStyleRecipe recipe in recipes)
            {
                GameObject[] consumers = recipe.ConsumerPrefabs;
                if (!CanReuse(recipe.OwnerPrefab) || consumers.Any(consumer => !CanReuse(consumer)))
                {
                    paths.UnionWith(consumers.Select(AssetDatabase.GetAssetPath));
                }
            }

            return paths;

            bool CanReuse(GameObject prefab)
            {
                if (!reusablePrefabs.TryGetValue(prefab, out bool reusable))
                {
                    reusable = StylePrefabInspectionPolicy.CanReuse(prefab);
                    reusablePrefabs.Add(prefab, reusable);
                }

                return reusable;
            }
        }

        private static StyleReview Measure(string phase, int ownerCount, Func<StyleReview> inspect)
        {
            var stopwatch = Stopwatch.StartNew();
            StyleReview review = inspect();
            stopwatch.Stop();
            TestContext.WriteLine(
                $"{phase}: {ownerCount} owners, {stopwatch.Elapsed.TotalMilliseconds:F1} ms, "
                + $"{review.OwnerPrefabLoadCount} owner loads, {review.ConsumerPrefabLoadCount} consumer loads, "
                + $"{review.AppliedOwnerPrefabCount} applied owners.");
            return review;
        }

        private static void AssertMatchesFull(StyleRecipeRegistry registry, StyleReview actual, int ownerCount)
        {
            StyleReview full = Measure("Full comparison", ownerCount, () => StyleRecipeProcessor.PreviewFull(registry));
            AssertReviewsEqual(full, actual);
        }

        private static void AssertReviewsEqual(StyleReview expected, StyleReview actual)
        {
            Assert.That(actual.State, Is.EqualTo(expected.State), actual.ToString());
            Assert.That(actual.Assets, Is.EqualTo(expected.Assets));
            Assert.That(actual.Changes, Is.EqualTo(expected.Changes));
            Assert.That(actual.Errors, Is.EqualTo(expected.Errors));
            Assert.That(actual.Fingerprint, Is.EqualTo(expected.Fingerprint));
        }

        private Dictionary<string, FileSnapshot> CaptureFiles()
        {
            return Directory.GetFiles(_testDirectory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetFullPath(path), path => new FileSnapshot(path), StringComparer.OrdinalIgnoreCase);
        }

        private void AssertFilesChangedOnly(IReadOnlyDictionary<string, FileSnapshot> before, params string[] allowedAssets)
        {
            Dictionary<string, FileSnapshot> after = CaptureFiles();
            Assert.That(after.Keys, Is.EquivalentTo(before.Keys), "The inspection created or deleted files.");
            string projectDirectory = Directory.GetParent(Application.dataPath).FullName;
            string[] allowed = allowedAssets.Select(path => Path.GetFullPath(Path.Combine(projectDirectory, path))).ToArray();
            string[] changed = before.Where(pair =>
                    pair.Value.LastWriteTime != after[pair.Key].LastWriteTime
                    || !pair.Value.Bytes.SequenceEqual(after[pair.Key].Bytes))
                .Select(pair => pair.Key)
                .ToArray();
            Assert.That(changed, Is.EquivalentTo(allowed), "Unexpected writes, including unchanged Prefabs or authoring assets.");
        }

        private void AssertUnmanagedValues(Fixture fixture, int index, Color expectedColor)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(OwnerPath(index));
            try
            {
                Image image = root.GetComponent<Image>();
                Assert.That(image.color, Is.EqualTo(expectedColor));
                Assert.That(image.raycastTarget, Is.False);
                Assert.That(image.preserveAspect, Is.True);
                var rect = (RectTransform)root.transform;
                Assert.That(rect.anchoredPosition, Is.EqualTo(ExpectedPosition(index)));
                Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(128f, 32f)));
                if (index % 10 == 1)
                {
                    TMP_Text text = root.GetComponentInChildren<TMP_Text>(true);
                    Assert.That(text.text, Is.EqualTo("Unmanaged label " + index));
                    Assert.That(text.raycastTarget, Is.False);
                    Assert.That(text.font, Is.EqualTo(fixture.Recipes[index].Texts[0].Style.Font));
                }

                if (index % 10 == 3)
                {
                    MonoBehaviour runtime = root.GetComponents<MonoBehaviour>().Single(component =>
                        component.GetType().FullName == "SuperHeroUnite.UI.ScaleTests.ScaleRuntimeOnlyBehaviour");
                    var serialized = new SerializedObject(runtime);
                    Assert.That(serialized.FindProperty("_marker").intValue, Is.EqualTo(73));
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private T CreateAsset<T>(string fileName)
            where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, _testRoot + "/" + fileName);
            return asset;
        }

        private static PrefabTargetReference Capture(GameObject root, Component component, PrefabTargetKind kind)
        {
            var target = new PrefabTargetReference();
            target.Configure(
                GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                PrefabTargetResolver.GetDisplayPath(root.transform, component.transform),
                component.GetType().FullName,
                kind);
            return target;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
            if (target is Object asset)
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private string OwnerPath(int index)
        {
            return _testRoot + "/" + OwnerName(index) + ".prefab";
        }

        private static string OwnerName(int index)
        {
            return $"Owner {index:D4}";
        }

        private static bool IsVariant(int index)
        {
            return index % 10 == 9;
        }

        private static Vector2 ExpectedPosition(int index)
        {
            int sourceIndex = IsVariant(index) ? index - 1 : index;
            return new Vector2(sourceIndex % 17, sourceIndex % 23);
        }

        private sealed class Fixture
        {
            public StyleRecipeRegistry Registry { get; set; }
            public PrefabStyleRecipe[] Recipes { get; set; }
            public ColorToken[] Tokens { get; set; }
        }

        private sealed class FileSnapshot
        {
            public byte[] Bytes { get; }
            public DateTime LastWriteTime { get; }

            public FileSnapshot(string path)
            {
                Bytes = File.ReadAllBytes(path);
                LastWriteTime = File.GetLastWriteTimeUtc(path);
            }
        }
    }
}
