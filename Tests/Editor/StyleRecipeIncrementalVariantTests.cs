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
    public sealed class StyleRecipeIncrementalVariantTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = "Assets/__SuperHeroUIIncrementalVariantTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
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
            AssetDatabase.Refresh();
        }

        [Test]
        public void UnsavedSpecializationOwnershipChangeInvalidatesBaseConsumerReview()
        {
            Fixture fixture = CreateFixture(true);
            WarmReadyPreview(fixture.Registry);

            SetPrivateField(fixture.VariantStyle, "_ownsPreserveAspect", false);

            AssertSpecializationOverrideIsRejected(fixture.Registry);
        }

        [Test]
        public void RemovingBaseRecipeInvalidatesPreviouslyValidSpecialization()
        {
            Fixture fixture = CreateFixture(true);
            WarmReadyPreview(fixture.Registry);

            SetPrivateField(fixture.VariantRecipe, "_baseRecipe", null);

            AssertSpecializationOverrideIsRejected(fixture.Registry);
        }

        [Test]
        public void ApplyIncludesInitiallyReadyVariantAfterItsBaseChanges()
        {
            Fixture fixture = CreateFixture(false);
            WarmReadyPreview(fixture.Registry);
            SetPrivateField(fixture.BaseToken, "_value", Color.red);

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Stale), preview.ToString());
            Assert.That(preview.ChangedOwnerPaths, Is.EqualTo(new[] { _testRoot + "/Base.prefab" }));
            AssertEquivalentToFull(fixture.Registry, preview);

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);

            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            Assert.That(applied.AppliedOwnerPrefabCount, Is.EqualTo(2));
            AssertPrefabColor(_testRoot + "/Base.prefab", Color.red);
            AssertPrefabColor(_testRoot + "/Variant.prefab", Color.white);
            AssertEquivalentToFull(fixture.Registry, applied);
            StyleReview repeated = StyleRecipeProcessor.Apply(fixture.Registry, applied);
            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready), repeated.ToString());
            Assert.That(repeated.AppliedOwnerPrefabCount, Is.Zero);
        }

        private static void WarmReadyPreview(StyleRecipeRegistry registry)
        {
            StyleReview first = StyleRecipeProcessor.Preview(registry);
            Assert.That(first.State, Is.EqualTo(StyleReviewState.Ready), first.ToString());
            StyleReview warm = StyleRecipeProcessor.Preview(registry);
            Assert.That(warm.State, Is.EqualTo(StyleReviewState.Ready), warm.ToString());
            Assert.That(warm.OwnerPrefabLoadCount, Is.Zero);
            Assert.That(warm.ConsumerPrefabLoadCount, Is.Zero);
        }

        private static void AssertSpecializationOverrideIsRejected(StyleRecipeRegistry registry)
        {
            StyleReview incremental = StyleRecipeProcessor.Preview(registry);
            Assert.That(incremental.State, Is.EqualTo(StyleReviewState.Error), incremental.ToString());
            Assert.That(incremental.Errors.Any(error =>
                error.Contains("m_PreserveAspect") && error.Contains("overrides a recipe-owned value")),
                Is.True,
                incremental.ToString());
            AssertEquivalentToFull(registry, incremental);
        }

        private static void AssertEquivalentToFull(StyleRecipeRegistry registry, StyleReview incremental)
        {
            StyleReview full = StyleRecipeProcessor.PreviewFull(registry);
            Assert.That(incremental.State, Is.EqualTo(full.State));
            Assert.That(incremental.Assets, Is.EqualTo(full.Assets));
            Assert.That(incremental.Changes, Is.EqualTo(full.Changes));
            Assert.That(incremental.Errors, Is.EqualTo(full.Errors));
            Assert.That(incremental.Fingerprint, Is.EqualTo(full.Fingerprint));
        }

        private Fixture CreateFixture(bool overridePreserveAspect)
        {
            string basePath = _testRoot + "/Base.prefab";
            var root = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, basePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            string variantPath = _testRoot + "/Variant.prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                if (overridePreserveAspect)
                {
                    Image image = instance.GetComponent<Image>();
                    image.preserveAspect = true;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                }

                PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            GameObject variantPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            ColorToken baseToken = CreateAsset<ColorToken>("Base Token.asset");
            ColorToken variantToken = CreateAsset<ColorToken>("Variant Token.asset");
            ImageStyle baseStyle = CreateImageStyle("Base Style.asset", baseToken, false);
            ImageStyle variantStyle = CreateImageStyle("Variant Style.asset", variantToken, overridePreserveAspect);
            PrefabStyleRecipe baseRecipe = CreateRecipe("Base Recipe.asset", basePrefab, baseStyle);
            PrefabStyleRecipe variantRecipe = CreateRecipe("Variant Recipe.asset", variantPrefab, variantStyle);
            SetPrivateField(baseRecipe, "_consumerPrefabs", new[] { variantPrefab });
            SetPrivateField(variantRecipe, "_baseRecipe", baseRecipe);
            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(registry, "_recipes", new[] { variantRecipe, baseRecipe });
            AssetDatabase.SaveAssets();
            return new Fixture(registry, variantRecipe, variantStyle, baseToken);
        }

        private ImageStyle CreateImageStyle(string fileName, ColorToken token, bool preserveAspect)
        {
            ImageStyle style = CreateAsset<ImageStyle>(fileName);
            SetPrivateField(style, "_tint", token);
            SetPrivateField(style, "_ownsPreserveAspect", true);
            SetPrivateField(style, "_preserveAspect", preserveAspect);
            return style;
        }

        private PrefabStyleRecipe CreateRecipe(string fileName, GameObject prefab, ImageStyle style)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            var target = new PrefabTargetReference();
            try
            {
                Image image = root.GetComponent<Image>();
                target.Configure(
                    GlobalObjectId.GetGlobalObjectIdSlow(image).ToString(),
                    PrefabTargetResolver.GetDisplayPath(root.transform, image.transform),
                    image.GetType().FullName,
                    PrefabTargetKind.Image);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var binding = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(binding, "_target", target);
            SetPrivateField(binding, "_style", style);
            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>(fileName);
            SetPrivateField(recipe, "_ownerPrefab", prefab);
            SetPrivateField(recipe, "_images", new[] { binding });
            return recipe;
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
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing test field {target.GetType().Name}.{fieldName}");
            field.SetValue(target, value);
            if (target is Object asset)
            {
                EditorUtility.SetDirty(asset);
            }
        }

        private static void AssertPrefabColor(string path, Color expected)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Assert.That(root.GetComponent<Image>().color, Is.EqualTo(expected));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private sealed class Fixture
        {
            public StyleRecipeRegistry Registry { get; }
            public PrefabStyleRecipe VariantRecipe { get; }
            public ImageStyle VariantStyle { get; }
            public ColorToken BaseToken { get; }

            public Fixture(
                StyleRecipeRegistry registry,
                PrefabStyleRecipe variantRecipe,
                ImageStyle variantStyle,
                ColorToken baseToken)
            {
                Registry = registry;
                VariantRecipe = variantRecipe;
                VariantStyle = variantStyle;
                BaseToken = baseToken;
            }
        }
    }
}
