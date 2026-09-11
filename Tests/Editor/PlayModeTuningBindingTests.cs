using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using TMPro;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class PlayModeTuningBindingTests
    {
        private readonly List<Object> _objects = new();
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = "Assets/__SuperHeroUITuningBindingTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null && !EditorUtility.IsPersistent(_objects[index]))
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
            AssetDatabase.DeleteAsset(_testRoot);
        }

        [Test]
        public void EnumeratesTypedImageSurfaceTextGraphicAndSelectableSources()
        {
            PrefabStyleRecipe recipe = CreateRecipe(CreateOwner());
            ColorToken color = CreateAsset<ColorToken>();
            SetPrivateField(color, "_value", Color.cyan);
            ImageStyle imageStyle = CreateImageStyle(color, true);
            ImageStyle outlineStyle = CreateImageStyle(color, false);
            TextStyle textStyle = CreateAsset<TextStyle>();
            SetPrivateField(textStyle, "_color", color);
            SurfaceStyle surfaceStyle = CreateAsset<SurfaceStyle>();
            SetPrivateField(surfaceStyle, "_fill", imageStyle);
            SetPrivateField(surfaceStyle, "_outline", outlineStyle);

            var image = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(image, "_target", Capture(recipe.OwnerPrefab, 0, typeof(Image), PrefabTargetKind.Image));
            SetPrivateField(image, "_style", imageStyle);
            SetPrivateField(recipe, "_images", new[] { image });
            var surface = new PrefabStyleRecipe.SurfaceBinding();
            SetPrivateField(surface, "_fillTarget", Capture(recipe.OwnerPrefab, 1, typeof(Image), PrefabTargetKind.Image));
            SetPrivateField(surface, "_outlineTarget", Capture(recipe.OwnerPrefab, 2, typeof(Image), PrefabTargetKind.Image));
            SetPrivateField(surface, "_style", surfaceStyle);
            SetPrivateField(recipe, "_surfaces", new[] { surface });
            var text = new PrefabStyleRecipe.TextBinding();
            SetPrivateField(text, "_target", Capture(recipe.OwnerPrefab, 0, typeof(TMP_Text), PrefabTargetKind.Text));
            SetPrivateField(text, "_style", textStyle);
            SetPrivateField(recipe, "_texts", new[] { text });
            var graphic = new PrefabStyleRecipe.GraphicColorBinding();
            SetPrivateField(graphic, "_target", Capture(recipe.OwnerPrefab, 0, typeof(RawImage), PrefabTargetKind.Graphic));
            SetPrivateField(graphic, "_color", color);
            SetPrivateField(recipe, "_graphicColors", new[] { graphic });
            ConfigureSelectable(recipe, color);

            IReadOnlyList<PlayModeTuningBinding> bindings = PlayModeTuningBinding.GetBindings(recipe);

            Assert.That(bindings.Count, Is.EqualTo(12));
            Assert.That(bindings.Count(binding => !binding.IsColor), Is.EqualTo(2));
            Assert.That(bindings.Where(binding => binding.IsColor).All(binding => binding.SourceAsset == color), Is.True);
            Assert.That(bindings.Where(binding => binding.IsColor).All(binding => binding.SourceColor == Color.cyan), Is.True);
            Assert.That(bindings.Where(binding => !binding.IsColor).All(binding => binding.SourceAsset == imageStyle), Is.True);
            Assert.That(bindings.Where(binding => !binding.IsColor).All(binding => binding.SourceFloat == 2.5f), Is.True);
            Assert.That(bindings.Select(binding => binding.Key).Distinct().Count(), Is.EqualTo(bindings.Count));
            foreach (PlayModeTuningBinding binding in bindings)
            {
                Assert.That(PlayModeTuningBinding.TryFindBinding(recipe, binding.Key, out PlayModeTuningBinding found), Is.True);
                Assert.That(found.Property, Is.EqualTo(binding.Property));
                Assert.That(found.SourceAsset, Is.SameAs(binding.SourceAsset));
            }
        }

        [Test]
        public void OmitsUnownedPpumMissingSourcesAndUncapturedTargets()
        {
            PrefabStyleRecipe recipe = CreateRecipe(CreateOwner());
            ImageStyle style = CreateImageStyle(CreateAsset<ColorToken>(), false);
            var valid = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(valid, "_target", Capture(recipe.OwnerPrefab, 0, typeof(Image), PrefabTargetKind.Image));
            SetPrivateField(valid, "_style", style);
            var missingTarget = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(missingTarget, "_style", style);
            var missingStyle = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(missingStyle, "_target", valid.Target);
            SetPrivateField(recipe, "_images", new[] { valid, missingTarget, missingStyle, null });
            SetPrivateField(recipe, "_surfaces", new PrefabStyleRecipe.SurfaceBinding[] { null });
            SetPrivateField(recipe, "_selectables", new PrefabStyleRecipe.SelectableBinding[] { null });

            IReadOnlyList<PlayModeTuningBinding> bindings = PlayModeTuningBinding.GetBindings(recipe);

            Assert.That(bindings.Count, Is.EqualTo(1));
            Assert.That(bindings[0].Property, Is.EqualTo(PlayModeTuningProperty.GraphicColor));
        }

        [Test]
        public void RejectsWrongComponentWrongOwnerAndPersistentOrPreviewTargets()
        {
            GameObject owner = CreateOwner();
            PlayModeTuningBinding binding = CreateImageBinding(CreateRecipe(owner), 0);
            GameObject instance = Track((GameObject)PrefabUtility.InstantiatePrefab(owner));
            Image image = instance.GetComponent<Image>();
            image.gameObject.SetActive(false);
            Assert.That(binding.ValidateTarget(image, out string error), Is.True, error);
            Assert.That(binding.ValidateTarget(instance.GetComponent<Button>(), out _), Is.False);
            Assert.That(binding.ValidateTarget(owner.GetComponent<Image>(), out _), Is.False);
            Assert.That(binding.ValidateTarget(instance.transform.GetChild(0).GetComponent<Image>(), out _), Is.False);
            GameObject other = Track((GameObject)PrefabUtility.InstantiatePrefab(CreateOwner("Other")));
            Assert.That(binding.ValidateTarget(other.GetComponent<Image>(), out _), Is.False);

            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(Image));
                SceneManager.MoveGameObjectToScene(previewObject, preview);
                Assert.That(binding.ValidateTarget(previewObject.GetComponent<Image>(), out _), Is.False);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Test]
        public void ExplicitUnlinkedClonesRemainSelectableUnderLinkedParents()
        {
            GameObject owner = CreateOwner();
            PlayModeTuningBinding binding = CreateImageBinding(CreateRecipe(owner), 0);
            GameObject clone = Track(Object.Instantiate(owner));
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(clone), Is.False);
            Assert.That(binding.ValidateTarget(clone.GetComponent<Image>(), out string error), Is.True, error);
            Assert.That(binding.ValidateTarget(clone.GetComponentInChildren<RawImage>(), out _), Is.False);

            GameObject linkedInstance = Track((GameObject)PrefabUtility.InstantiatePrefab(owner));
            clone.transform.SetParent(linkedInstance.transform, false);
            Assert.That(binding.ValidateTarget(clone.GetComponent<Image>(), out error), Is.True, error);
            var addedChild = new GameObject("Added", typeof(RectTransform), typeof(Image));
            addedChild.transform.SetParent(linkedInstance.transform, false);
            Assert.That(binding.ValidateTarget(addedChild.GetComponent<Image>(), out error), Is.True, error);
            var addedComponent = linkedInstance.GetComponentInChildren<RawImage>().gameObject.AddComponent<Button>();
            ConfigureSelectable(binding.Recipe, CreateAsset<ColorToken>());
            PlayModeTuningBinding selectable = PlayModeTuningBinding.GetBindings(binding.Recipe)
                .Single(item => item.Property == PlayModeTuningProperty.NormalColor);
            Assert.That(selectable.ValidateTarget(addedComponent, out _), Is.False);
        }

        [Test]
        public void NestedInstancesAndVariantsRetainTheCapturedOwnerIdentity()
        {
            GameObject source = CreateOwner();
            GameObject nestedRoot = Track(new GameObject("Nested", typeof(RectTransform)));
            PrefabUtility.InstantiatePrefab(source, nestedRoot.transform);
            PrefabUtility.InstantiatePrefab(source, nestedRoot.transform);
            GameObject nested = PrefabUtility.SaveAsPrefabAsset(nestedRoot, _testRoot + "/Nested.prefab");
            PlayModeTuningBinding nestedBinding = CreateImageBinding(CreateRecipe(source), 0);
            GameObject nestedInstance = Track((GameObject)PrefabUtility.InstantiatePrefab(nested));
            Image first = nestedInstance.transform.GetChild(0).GetComponent<Image>();
            Image second = nestedInstance.transform.GetChild(1).GetComponent<Image>();
            Assert.That(nestedBinding.ValidateTarget(first, out string error), Is.True, error);
            Assert.That(nestedBinding.ValidateTarget(second, out error), Is.True, error);
            PlayModeTuningBinding invalidOuterBinding = CreateImageBinding(CreateRecipe(nested), 0);
            Assert.That(invalidOuterBinding.ValidateTarget(first, out _), Is.False);
            GameObject plainInstance = Track((GameObject)PrefabUtility.InstantiatePrefab(source));
            Assert.That(nestedBinding.ValidateTarget(plainInstance.GetComponent<Image>(), out error), Is.True, error);

            GameObject variant = PrefabUtility.SaveAsPrefabAsset(plainInstance, _testRoot + "/Variant.prefab");
            PlayModeTuningBinding variantBinding = CreateImageBinding(CreateRecipe(variant), 0);
            GameObject variantInstance = Track((GameObject)PrefabUtility.InstantiatePrefab(variant));
            Assert.That(variantBinding.ValidateTarget(variantInstance.GetComponent<Image>(), out error), Is.True, error);
            Assert.That(variantBinding.ValidateTarget(variantInstance.transform.GetChild(0).GetComponent<Image>(), out _), Is.False);
            GameObject sourceInstance = Track((GameObject)PrefabUtility.InstantiatePrefab(source));
            Assert.That(variantBinding.ValidateTarget(sourceInstance.GetComponent<Image>(), out _), Is.False);

            GameObject derivedVariant = PrefabUtility.SaveAsPrefabAsset(variantInstance, _testRoot + "/Derived Variant.prefab");
            PlayModeTuningBinding derivedBinding = CreateImageBinding(CreateRecipe(derivedVariant), 0);
            GameObject derivedInstance = Track((GameObject)PrefabUtility.InstantiatePrefab(derivedVariant));
            Assert.That(derivedBinding.ValidateTarget(derivedInstance.GetComponent<Image>(), out error), Is.True, error);
            GameObject parentVariantInstance = Track((GameObject)PrefabUtility.InstantiatePrefab(variant));
            Assert.That(derivedBinding.ValidateTarget(parentVariantInstance.GetComponent<Image>(), out _), Is.False);
        }

        [TestCase((int)PlayModeTuningProperty.NormalColor)]
        [TestCase((int)PlayModeTuningProperty.HighlightedColor)]
        [TestCase((int)PlayModeTuningProperty.PressedColor)]
        [TestCase((int)PlayModeTuningProperty.SelectedColor)]
        [TestCase((int)PlayModeTuningProperty.DisabledColor)]
        public void SelectableWritesOnlyTheChosenState(int propertyValue)
        {
            var property = (PlayModeTuningProperty)propertyValue;
            PrefabStyleRecipe recipe = CreateRecipe(CreateOwner());
            ConfigureSelectable(recipe, CreateAsset<ColorToken>());
            PlayModeTuningBinding binding = PlayModeTuningBinding.GetBindings(recipe).Single(item => item.Property == property);
            GameObject instance = Track(Object.Instantiate(recipe.OwnerPrefab));
            Button button = instance.GetComponent<Button>();
            ColorBlock original = button.colors;
            original.normalColor = Color.red;
            original.highlightedColor = Color.green;
            original.pressedColor = Color.blue;
            original.selectedColor = Color.yellow;
            original.disabledColor = Color.gray;
            original.colorMultiplier = 3f;
            original.fadeDuration = 0.7f;
            button.colors = original;

            binding.WriteColor(button, Color.magenta);

            Assert.That(binding.ReadColor(button), Is.EqualTo(Color.magenta));
            foreach (PlayModeTuningBinding other in PlayModeTuningBinding.GetBindings(recipe))
            {
                if (other.Property != property)
                {
                    Color actual = other.ReadColor(button);
                    button.colors = original;
                    Color expected = other.ReadColor(button);
                    binding.WriteColor(button, Color.magenta);
                    Assert.That(actual, Is.EqualTo(expected));
                }
            }

            Assert.That(button.colors.colorMultiplier, Is.EqualTo(original.colorMultiplier));
            Assert.That(button.colors.fadeDuration, Is.EqualTo(original.fadeDuration));
        }

        [Test]
        public void PpumAndTintWritesPreserveOtherImageValuesAndAuthoredAssets()
        {
            PrefabStyleRecipe recipe = CreateRecipe(CreateOwner());
            PlayModeTuningBinding tint = CreateImageBinding(recipe, 0);
            PlayModeTuningBinding ppum = PlayModeTuningBinding.GetBindings(recipe).Single(item => !item.IsColor);
            GameObject instance = Track(Object.Instantiate(recipe.OwnerPrefab));
            Image image = instance.GetComponent<Image>();
            image.fillCenter = false;
            image.raycastTarget = false;
            image.preserveAspect = true;

            tint.WriteColor(image, Color.black);
            ppum.WriteFloat(image, 4f);

            Assert.That(tint.ReadColor(image), Is.EqualTo(Color.black));
            Assert.That(ppum.ReadFloat(image), Is.EqualTo(4f));
            Assert.That(tint.SourceColor, Is.EqualTo(Color.white));
            Assert.That(ppum.SourceFloat, Is.EqualTo(2.5f));
            Assert.That(image.fillCenter, Is.False);
            Assert.That(image.raycastTarget, Is.False);
            Assert.That(image.preserveAspect, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => ppum.WriteFloat(image, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => ppum.WriteFloat(image, 0f));
            Assert.Throws<InvalidOperationException>(() => ppum.WriteFloat(recipe.OwnerPrefab.GetComponent<Image>(), 5f));
        }

        [Test]
        public void RestoresLiveSnapshotAfterRecaptureAndRecipeDeletion()
        {
            PrefabStyleRecipe recipe = CreateRecipe(CreateOwner());
            PlayModeTuningBinding tint = CreateImageBinding(recipe, 0);
            PlayModeTuningBinding ppum = PlayModeTuningBinding.GetBindings(recipe).Single(item => !item.IsColor);
            GameObject instance = Track(Object.Instantiate(recipe.OwnerPrefab));
            Image image = instance.GetComponent<Image>();
            Color originalColor = image.color;
            float originalPpum = image.pixelsPerUnitMultiplier;
            tint.WriteColor(image, Color.black);
            ppum.WriteFloat(image, 4f);
            tint.TargetReference.Configure(string.Empty, "Recaptured", typeof(RawImage).FullName, PrefabTargetKind.Graphic);
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(recipe));

            tint.RestoreColor(image, originalColor);
            ppum.RestoreFloat(image, originalPpum);

            Assert.That(image.color, Is.EqualTo(originalColor));
            Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(originalPpum));
            Assert.Throws<InvalidOperationException>(() => tint.RestoreColor(instance.GetComponentInChildren<RawImage>(), Color.red));
            Assert.Throws<InvalidOperationException>(() => ppum.RestoreFloat(CreateOwner("Persistent").GetComponent<Image>(), 1f));
        }

        private PlayModeTuningBinding CreateImageBinding(PrefabStyleRecipe recipe, int imageIndex)
        {
            var binding = new PrefabStyleRecipe.ImageBinding();
            SetPrivateField(binding, "_target", Capture(recipe.OwnerPrefab, imageIndex, typeof(Image), PrefabTargetKind.Image));
            SetPrivateField(binding, "_style", CreateImageStyle(CreateAsset<ColorToken>(), true));
            SetPrivateField(recipe, "_images", new[] { binding });
            return PlayModeTuningBinding.GetBindings(recipe).Single(item => item.IsColor);
        }

        private void ConfigureSelectable(PrefabStyleRecipe recipe, ColorToken token)
        {
            SelectableStyle style = CreateAsset<SelectableStyle>();
            foreach (string field in new[] { "_normal", "_highlighted", "_pressed", "_selected", "_disabled" })
            {
                SetPrivateField(style, field, token);
            }

            var binding = new PrefabStyleRecipe.SelectableBinding();
            SetPrivateField(binding, "_target", Capture(recipe.OwnerPrefab, 0, typeof(Button), PrefabTargetKind.Selectable));
            SetPrivateField(binding, "_style", style);
            SetPrivateField(recipe, "_selectables", new[] { binding });
        }

        private ImageStyle CreateImageStyle(ColorToken tint, bool ownsPpum)
        {
            ImageStyle style = CreateAsset<ImageStyle>();
            SetPrivateField(style, "_tint", tint);
            SetPrivateField(style, "_ownsPixelsPerUnitMultiplier", ownsPpum);
            SetPrivateField(style, "_pixelsPerUnitMultiplier", 2.5f);
            return style;
        }

        private PrefabStyleRecipe CreateRecipe(GameObject owner)
        {
            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>();
            SetPrivateField(recipe, "_ownerPrefab", owner);
            return recipe;
        }

        private T CreateAsset<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, _testRoot + "/" + typeof(T).Name + Guid.NewGuid().ToString("N") + ".asset");
            return asset;
        }

        private GameObject CreateOwner(string name = "Owner")
        {
            GameObject root = Track(new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)));
            for (int index = 0; index < 2; index++)
            {
                var child = new GameObject("Image " + index, typeof(RectTransform), typeof(Image));
                child.transform.SetParent(root.transform, false);
            }

            var text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(root.transform, false);
            var graphic = new GameObject("Graphic", typeof(RectTransform), typeof(RawImage));
            graphic.transform.SetParent(root.transform, false);
            return PrefabUtility.SaveAsPrefabAsset(root, _testRoot + "/" + name + ".prefab");
        }

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }

        private static PrefabTargetReference Capture(GameObject owner, int index, Type type, PrefabTargetKind kind)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(owner));
            try
            {
                Component component = root.GetComponentsInChildren(type, true)[index];
                var target = new PrefabTargetReference();
                target.Configure(GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                    PrefabTargetResolver.GetDisplayPath(root.transform, component.transform),
                    component.GetType().FullName, kind);
                return target;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            FieldInfo fieldInfo = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(fieldInfo, Is.Not.Null, field);
            fieldInfo.SetValue(target, value);
        }
    }
}
