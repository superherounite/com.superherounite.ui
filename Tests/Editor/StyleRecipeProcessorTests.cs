using System;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using TMPro;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipeProcessorTests
    {
        private const string TestRootPrefix = "__SuperHeroUIEditorTests_";

        private static string s_testRoot;
        private static string OwnerPath => s_testRoot + "/Owner.prefab";
        private static string VariantPath => s_testRoot + "/Owner Variant.prefab";
        private static string ConsumerPath => s_testRoot + "/Consumer.prefab";

        [SetUp]
        public void SetUp()
        {
            s_testRoot = "Assets/" + TestRootPrefix + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(s_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(s_testRoot))
            {
                AssetDatabase.DeleteAsset(s_testRoot);
            }

            s_testRoot = null;
            AssetDatabase.Refresh();
        }

        [Test]
        public void PreviewAndApplyAreReadOnlyAndIdempotent()
        {
            Fixture fixture = CreateFixture();
            string beforePreview = File.ReadAllText(OwnerPath);

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Stale));
            Assert.That(preview.Changes, Is.Not.Empty);
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(beforePreview));

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);

            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready));
            Assert.That(applied.Changes, Is.Empty);
            using (LoadedPrefab owner = LoadedPrefab.Open(OwnerPath))
            {
                Assert.That(owner.Root.GetComponent<Image>().color, Is.EqualTo(fixture.FillColor));
                Assert.That(
                    owner.Root.transform.GetChild(0).GetComponent<Image>().color,
                    Is.EqualTo(fixture.OutlineColor));
                Assert.That(
                    owner.Root.transform.GetChild(1).GetComponent<Image>().type,
                    Is.EqualTo(Image.Type.Sliced));
                Assert.That(
                    owner.Root.transform.GetChild(1).GetComponent<Image>().preserveAspect,
                    Is.True);
                Assert.That(
                    owner.Root.transform.GetChild(1).GetComponent<Image>().fillCenter,
                    Is.False);
                Assert.That(
                    owner.Root.transform.GetChild(1).GetComponent<Image>().pixelsPerUnitMultiplier,
                    Is.EqualTo(2.5f));
                Assert.That(
                    owner.Root.transform.GetChild(1).GetComponent<Image>().sprite,
                    Is.EqualTo(fixture.IconSprite));
                Assert.That(
                    owner.Root.transform.GetChild(1).GetComponent<Image>().raycastTarget,
                    Is.False);
                TMP_Text label = owner.Root.transform.GetChild(2).GetComponent<TMP_Text>();
                Assert.That(label.color, Is.EqualTo(fixture.FillColor));
                Assert.That(label.fontSize, Is.EqualTo(20f));
                Assert.That(label.fontStyle, Is.EqualTo(FontStyles.Bold));
                Assert.That(label.enableAutoSizing, Is.True);
                Assert.That(label.fontSizeMin, Is.EqualTo(12f));
                Assert.That(label.fontSizeMax, Is.EqualTo(24f));
                Assert.That(label.fontSharedMaterial, Is.EqualTo(label.font.material));
                Assert.That(label.text, Is.EqualTo("Sample"));
                Assert.That(label.characterSpacing, Is.EqualTo(1f));
                Assert.That(label.wordSpacing, Is.EqualTo(2f));
                Assert.That(label.lineSpacing, Is.EqualTo(3f));
                Assert.That(label.paragraphSpacing, Is.EqualTo(4f));
                Button button = owner.Root.GetComponent<Button>();
                Assert.That(button.transition, Is.EqualTo(Selectable.Transition.ColorTint));
                Assert.That(button.colors.normalColor, Is.EqualTo(fixture.FillColor));
                Assert.That(button.colors.highlightedColor, Is.EqualTo(fixture.OutlineColor));
                Assert.That(button.colors.pressedColor, Is.EqualTo(fixture.FillColor));
                Assert.That(button.colors.selectedColor, Is.EqualTo(fixture.OutlineColor));
                Assert.That(button.colors.disabledColor, Is.EqualTo(fixture.FillColor));
                Assert.That(button.colors.colorMultiplier, Is.EqualTo(1.5f));
                Assert.That(button.colors.fadeDuration, Is.EqualTo(0.25f));
                Assert.That(
                    ((RectTransform)owner.Root.transform).anchoredPosition,
                    Is.EqualTo(new Vector2(11f, 22f)));
                Assert.That(
                    owner.Root.transform.GetChild(3).GetComponent<Image>().color,
                    Is.EqualTo(fixture.OutlineColor));
                Assert.That(
                    owner.Root.GetComponentsInChildren<Component>(true)
                        .Any(component => component != null
                            && component.GetType().Assembly.GetName().Name
                                == "SuperHeroUnite.UI.Editor"),
                    Is.False);
            }

            StyleReview secondApply = StyleRecipeProcessor.Apply(fixture.Registry, applied);
            Assert.That(secondApply.State, Is.EqualTo(StyleReviewState.Ready));
            Assert.That(secondApply.Changes, Is.Empty);
        }

        [Test]
        public void ApplyRejectsAStalePreview()
        {
            Fixture fixture = CreateFixture();
            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            SetPrivateField(fixture.FillToken, "_value", Color.magenta);
            EditorUtility.SetDirty(fixture.FillToken);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(fixture.Registry, preview));

            StringAssert.Contains("changed after Preview", exception.Message);
        }

        [Test]
        public void DuplicatePropertyOwnershipIsAnError()
        {
            Fixture fixture = CreateFixture();
            var duplicate = new PrefabStyleRecipe.GraphicColorBinding();
            var duplicateTarget = new PrefabTargetReference();
            duplicateTarget.Configure(
                fixture.FillTarget.GlobalObjectId,
                fixture.FillTarget.DisplayPath,
                fixture.FillTarget.ComponentType,
                PrefabTargetKind.Graphic);
            SetPrivateField(duplicate, "_target", duplicateTarget);
            SetPrivateField(duplicate, "_color", fixture.FillToken);
            SetPrivateField(
                fixture.Recipe,
                "_graphicColors",
                new[] { duplicate });
            EditorUtility.SetDirty(fixture.Recipe);
            StyleReview review = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(review.State, Is.EqualTo(StyleReviewState.Error));
            Assert.That(
                review.Errors.Any(error => error.Contains("Duplicate style ownership")),
                Is.True);
        }

        [Test]
        public void ConsumerOverrideIsAnError()
        {
            Fixture fixture = CreateFixture();
            CreateConsumerWithFillOverride(fixture.OwnerPrefab);
            GameObject consumer = AssetDatabase.LoadAssetAtPath<GameObject>(ConsumerPath);
            SetPrivateField(fixture.Recipe, "_consumerPrefabs", new[] { consumer });
            EditorUtility.SetDirty(fixture.Recipe);
            StyleReview review = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(review.State, Is.EqualTo(StyleReviewState.Error));
            Assert.That(
                review.Errors.Any(error => error.Contains("overrides a recipe-owned value")),
                Is.True,
                review.ToString());
        }

        [Test]
        public void MissingTargetIsAnError()
        {
            Fixture fixture = CreateFixture();
            SetPrivateField(fixture.FillTarget, "_globalObjectId", string.Empty);
            EditorUtility.SetDirty(fixture.Recipe);
            StyleReview review = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(review.State, Is.EqualTo(StyleReviewState.Error));
            Assert.That(
                review.Errors.Any(error => error.Contains("has not been captured")),
                Is.True);
        }

        [Test]
        public void ConsumerOverrideThroughVariantIsAnError()
        {
            Fixture fixture = CreateFixture();
            GameObject variantPrefab = CreateOwnerVariantWithFillOverride(fixture.OwnerPrefab);
            CreateConsumer(variantPrefab);
            GameObject consumer = AssetDatabase.LoadAssetAtPath<GameObject>(ConsumerPath);
            SetPrivateField(fixture.Recipe, "_consumerPrefabs", new[] { consumer });
            EditorUtility.SetDirty(fixture.Recipe);
            StyleReview review = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(review.State, Is.EqualTo(StyleReviewState.Error));
            Assert.That(
                review.Errors.Any(error => error.Contains("overrides a recipe-owned value")),
                Is.True,
                review.ToString());
        }

        [Test]
        public void VariantOwnerWithConsumerCanReachReady()
        {
            Fixture fixture = CreateFixture();
            GameObject variantPrefab = CreateOwnerVariant(fixture.OwnerPrefab);
            CreateConsumer(variantPrefab);
            GameObject consumer = AssetDatabase.LoadAssetAtPath<GameObject>(ConsumerPath);
            PrefabTargetReference target;
            using (LoadedPrefab variant = LoadedPrefab.Open(VariantPath))
            {
                target = Capture(
                    variant.Root,
                    variant.Root.GetComponent<Image>(),
                    PrefabTargetKind.Image);
            }

            ImageStyle style = AssetDatabase.LoadAssetAtPath<ImageStyle>(
                s_testRoot + "/Fill Image.asset");
            var binding = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(binding, "_target", target);
            SetPrivateField(binding, "_style", style);
            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>("Variant Recipe.asset");
            SetPrivateField(recipe, "_ownerPrefab", variantPrefab);
            SetPrivateField(recipe, "_consumerPrefabs", new[] { consumer });
            SetPrivateField(recipe, "_images", new[] { binding });
            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Variant Registry.asset");
            SetPrivateField(registry, "_recipes", new[] { recipe });
            EditorUtility.SetDirty(recipe);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();

            StyleReview preview = StyleRecipeProcessor.Preview(registry);
            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Stale), preview.ToString());
            StyleReview applied = StyleRecipeProcessor.Apply(registry, preview);

            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            using (LoadedPrefab variant = LoadedPrefab.Open(VariantPath))
            {
                Assert.That(
                    variant.Root.GetComponent<Image>().color,
                    Is.EqualTo(fixture.FillColor));
            }
        }

        [Test]
        public void InvalidPixelsPerUnitMultiplierIsAnError()
        {
            Fixture fixture = CreateFixture();
            ImageStyle iconStyle = AssetDatabase.LoadAssetAtPath<ImageStyle>(
                s_testRoot + "/Icon Image.asset");
            SetPrivateField(iconStyle, "_pixelsPerUnitMultiplier", 0.001f);
            EditorUtility.SetDirty(iconStyle);
            StyleReview review = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(review.State, Is.EqualTo(StyleReviewState.Error));
            Assert.That(
                review.Errors.Any(error => error.Contains("at least 0.01")),
                Is.True);
        }

        [Test]
        public void GraphicColorRejectsSpecializedTmpColor()
        {
            Fixture fixture = CreateFixture();
            PrefabTargetReference textTarget = fixture.Recipe.Texts[0].Target;
            var graphicTarget = new PrefabTargetReference();
            graphicTarget.Configure(
                textTarget.GlobalObjectId,
                textTarget.DisplayPath,
                textTarget.ComponentType,
                PrefabTargetKind.Graphic);
            var graphicBinding = new PrefabStyleRecipe.GraphicColorBinding();
            SetPrivateField(graphicBinding, "_target", graphicTarget);
            SetPrivateField(graphicBinding, "_color", fixture.FillToken);
            SetPrivateField(fixture.Recipe, "_graphicColors", new[] { graphicBinding });
            EditorUtility.SetDirty(fixture.Recipe);
            StyleReview review = StyleRecipeProcessor.Preview(fixture.Registry);

            Assert.That(review.State, Is.EqualTo(StyleReviewState.Error));
            Assert.That(
                review.Errors.Any(error => error.Contains("specialized color property")),
                Is.True);
        }

        private static Fixture CreateFixture()
        {
            Color fillColor = new(0.12f, 0.24f, 0.36f, 1f);
            Color outlineColor = new(0.72f, 0.34f, 0.18f, 1f);
            ColorToken fillToken = CreateAsset<ColorToken>("Fill Color.asset");
            ColorToken outlineToken = CreateAsset<ColorToken>("Outline Color.asset");
            SetPrivateField(fillToken, "_value", fillColor);
            SetPrivateField(outlineToken, "_value", outlineColor);

            ImageStyle fillStyle = CreateAsset<ImageStyle>("Fill Image.asset");
            ImageStyle outlineStyle = CreateAsset<ImageStyle>("Outline Image.asset");
            ImageStyle iconStyle = CreateAsset<ImageStyle>("Icon Image.asset");
            Sprite iconSprite = CreateSpriteAsset("Icon.png");
            SetPrivateField(fillStyle, "_tint", fillToken);
            SetPrivateField(outlineStyle, "_tint", outlineToken);
            SetPrivateField(iconStyle, "_tint", outlineToken);
            SetPrivateField(iconStyle, "_ownsSprite", true);
            SetPrivateField(iconStyle, "_sprite", iconSprite);
            SetPrivateField(iconStyle, "_ownsType", true);
            SetPrivateField(iconStyle, "_type", Image.Type.Sliced);
            SetPrivateField(iconStyle, "_ownsPreserveAspect", true);
            SetPrivateField(iconStyle, "_preserveAspect", true);
            SetPrivateField(iconStyle, "_ownsFillCenter", true);
            SetPrivateField(iconStyle, "_fillCenter", false);
            SetPrivateField(iconStyle, "_ownsPixelsPerUnitMultiplier", true);
            SetPrivateField(iconStyle, "_pixelsPerUnitMultiplier", 2.5f);

            SurfaceStyle surfaceStyle = CreateAsset<SurfaceStyle>("Surface.asset");
            SetPrivateField(surfaceStyle, "_fill", fillStyle);
            SetPrivateField(surfaceStyle, "_outline", outlineStyle);

            CreateOwnerPrefab();
            GameObject ownerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OwnerPath);
            PrefabTargetReference fillTarget;
            PrefabTargetReference outlineTarget;
            PrefabTargetReference iconTarget;
            PrefabTargetReference textTarget;
            PrefabTargetReference selectableTarget;
            PrefabTargetReference decorationTarget;
            TMP_FontAsset font;
            using (LoadedPrefab owner = LoadedPrefab.Open(OwnerPath))
            {
                fillTarget = Capture(
                    owner.Root,
                    owner.Root.GetComponent<Image>(),
                    PrefabTargetKind.Image);
                outlineTarget = Capture(
                    owner.Root,
                    owner.Root.transform.GetChild(0).GetComponent<Image>(),
                    PrefabTargetKind.Image);
                iconTarget = Capture(
                    owner.Root,
                    owner.Root.transform.GetChild(1).GetComponent<Image>(),
                    PrefabTargetKind.Image);
                TMP_Text label = owner.Root.transform.GetChild(2).GetComponent<TMP_Text>();
                font = label.font;
                textTarget = Capture(owner.Root, label, PrefabTargetKind.Text);
                selectableTarget = Capture(
                    owner.Root,
                    owner.Root.GetComponent<Button>(),
                    PrefabTargetKind.Selectable);
                decorationTarget = Capture(
                    owner.Root,
                    owner.Root.transform.GetChild(3).GetComponent<Image>(),
                    PrefabTargetKind.Graphic);
            }

            Assert.That(font, Is.Not.Null, "TextMesh Pro default font is unavailable.");

            var surfaceBinding = new PrefabStyleRecipe.SurfaceBinding();
            SetPrivateField(surfaceBinding, "_fillTarget", fillTarget);
            SetPrivateField(surfaceBinding, "_outlineTarget", outlineTarget);
            SetPrivateField(surfaceBinding, "_style", surfaceStyle);

            var imageBinding = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(imageBinding, "_target", iconTarget);
            SetPrivateField(imageBinding, "_style", iconStyle);

            TextStyle textStyle = CreateAsset<TextStyle>("Body Text.asset");
            SetPrivateField(textStyle, "_font", font);
            SetPrivateField(textStyle, "_color", fillToken);
            SetPrivateField(textStyle, "_fontSize", 20f);
            SetPrivateField(textStyle, "_fontStyle", FontStyles.Bold);
            SetPrivateField(textStyle, "_enableAutoSizing", true);
            SetPrivateField(textStyle, "_fontSizeMin", 12f);
            SetPrivateField(textStyle, "_fontSizeMax", 24f);
            SetPrivateField(textStyle, "_characterSpacing", 1f);
            SetPrivateField(textStyle, "_wordSpacing", 2f);
            SetPrivateField(textStyle, "_lineSpacing", 3f);
            SetPrivateField(textStyle, "_paragraphSpacing", 4f);
            var textBinding = new PrefabStyleRecipe.TextBinding();
            SetPrivateField(textBinding, "_target", textTarget);
            SetPrivateField(textBinding, "_style", textStyle);

            SelectableStyle selectableStyle = CreateAsset<SelectableStyle>("Button State.asset");
            SetPrivateField(selectableStyle, "_normal", fillToken);
            SetPrivateField(selectableStyle, "_highlighted", outlineToken);
            SetPrivateField(selectableStyle, "_pressed", fillToken);
            SetPrivateField(selectableStyle, "_selected", outlineToken);
            SetPrivateField(selectableStyle, "_disabled", fillToken);
            SetPrivateField(selectableStyle, "_colorMultiplier", 1.5f);
            SetPrivateField(selectableStyle, "_fadeDuration", 0.25f);
            var selectableBinding = new PrefabStyleRecipe.SelectableBinding();
            SetPrivateField(selectableBinding, "_target", selectableTarget);
            SetPrivateField(selectableBinding, "_style", selectableStyle);

            var graphicColorBinding = new PrefabStyleRecipe.GraphicColorBinding();
            SetPrivateField(graphicColorBinding, "_target", decorationTarget);
            SetPrivateField(graphicColorBinding, "_color", outlineToken);

            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>("Recipe.asset");
            SetPrivateField(recipe, "_ownerPrefab", ownerPrefab);
            SetPrivateField(recipe, "_graphicColors", new[] { graphicColorBinding });
            SetPrivateField(recipe, "_images", new[] { imageBinding });
            SetPrivateField(recipe, "_surfaces", new[] { surfaceBinding });
            SetPrivateField(recipe, "_texts", new[] { textBinding });
            SetPrivateField(recipe, "_selectables", new[] { selectableBinding });

            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(registry, "_recipes", new[] { recipe });
            EditorUtility.SetDirty(fillToken);
            EditorUtility.SetDirty(outlineToken);
            EditorUtility.SetDirty(fillStyle);
            EditorUtility.SetDirty(outlineStyle);
            EditorUtility.SetDirty(iconStyle);
            EditorUtility.SetDirty(surfaceStyle);
            EditorUtility.SetDirty(textStyle);
            EditorUtility.SetDirty(selectableStyle);
            EditorUtility.SetDirty(recipe);
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();

            return new Fixture(
                registry,
                recipe,
                ownerPrefab,
                fillToken,
                fillTarget,
                fillColor,
                outlineColor,
                iconSprite);
        }

        private static void CreateOwnerPrefab()
        {
            var root = new GameObject(
                "Surface",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var outline = new GameObject(
                "Outline",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            outline.transform.SetParent(root.transform, false);
            var icon = new GameObject(
                "Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            icon.transform.SetParent(root.transform, false);
            var label = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            label.transform.SetParent(root.transform, false);
            var decoration = new GameObject(
                "Decoration",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            decoration.transform.SetParent(root.transform, false);
            root.GetComponent<Image>().color = Color.white;
            outline.GetComponent<Image>().color = Color.white;
            icon.GetComponent<Image>().color = Color.white;
            icon.GetComponent<Image>().raycastTarget = false;
            decoration.GetComponent<Image>().color = Color.white;
            TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
            text.text = "Sample";
            text.color = Color.white;
            text.fontSize = 10f;
            text.fontSharedMaterial = null;
            root.GetComponent<Button>().targetGraphic = root.GetComponent<Image>();
            root.GetComponent<Button>().transition = Selectable.Transition.None;
            ((RectTransform)root.transform).anchoredPosition = new Vector2(11f, 22f);
            PrefabUtility.SaveAsPrefabAsset(root, OwnerPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void CreateConsumerWithFillOverride(GameObject ownerPrefab)
        {
            var root = new GameObject("Consumer", typeof(RectTransform));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(ownerPrefab);
            instance.transform.SetParent(root.transform, false);
            Image fill = instance.GetComponent<Image>();
            fill.color = Color.black;
            PrefabUtility.RecordPrefabInstancePropertyModifications(fill);
            PrefabUtility.SaveAsPrefabAsset(root, ConsumerPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static GameObject CreateOwnerVariantWithFillOverride(GameObject ownerPrefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(ownerPrefab);
            Image fill = instance.GetComponent<Image>();
            fill.color = Color.black;
            PrefabUtility.RecordPrefabInstancePropertyModifications(fill);
            PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            UnityEngine.Object.DestroyImmediate(instance);
            return AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        }

        private static GameObject CreateOwnerVariant(GameObject ownerPrefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(ownerPrefab);
            PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            UnityEngine.Object.DestroyImmediate(instance);
            return AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        }

        private static void CreateConsumer(GameObject nestedPrefab)
        {
            var root = new GameObject("Consumer", typeof(RectTransform));
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(nestedPrefab);
            instance.transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, ConsumerPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static Sprite CreateSpriteAsset(string fileName)
        {
            string path = s_testRoot + "/" + fileName;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[]
            {
                Color.white,
                Color.white,
                Color.white,
                Color.white,
            });
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.That(sprite, Is.Not.Null);
            return sprite;
        }

        private static PrefabTargetReference Capture(
            GameObject root,
            Component component,
            PrefabTargetKind expectedKind)
        {
            var target = new PrefabTargetReference();
            target.Configure(
                GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                PrefabTargetResolver.GetDisplayPath(root.transform, component.transform),
                component.GetType().FullName,
                expectedKind);
            return target;
        }

        private static T CreateAsset<T>(string fileName)
            where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, s_testRoot + "/" + fileName);
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

        private sealed class Fixture
        {
            public StyleRecipeRegistry Registry { get; }
            public PrefabStyleRecipe Recipe { get; }
            public GameObject OwnerPrefab { get; }
            public ColorToken FillToken { get; }
            public PrefabTargetReference FillTarget { get; }
            public Color FillColor { get; }
            public Color OutlineColor { get; }
            public Sprite IconSprite { get; }

            public Fixture(
                StyleRecipeRegistry registry,
                PrefabStyleRecipe recipe,
                GameObject ownerPrefab,
                ColorToken fillToken,
                PrefabTargetReference fillTarget,
                Color fillColor,
                Color outlineColor,
                Sprite iconSprite)
            {
                Registry = registry;
                Recipe = recipe;
                OwnerPrefab = ownerPrefab;
                FillToken = fillToken;
                FillTarget = fillTarget;
                FillColor = fillColor;
                OutlineColor = outlineColor;
                IconSprite = iconSprite;
            }
        }

        private sealed class LoadedPrefab : IDisposable
        {
            public GameObject Root { get; }

            private LoadedPrefab(GameObject root)
            {
                Root = root;
            }

            public static LoadedPrefab Open(string path)
            {
                return new LoadedPrefab(PrefabUtility.LoadPrefabContents(path));
            }

            public void Dispose()
            {
                PrefabUtility.UnloadPrefabContents(Root);
            }
        }
    }
}
