using System;
using System.IO;
using System.Reflection;

using NUnit.Framework;

using TMPro;

using UnityEditor;
using UnityEditor.Events;

using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleOverrideRepairTests
    {
        private string _testRoot;

        private string OwnerPath => _testRoot + "/Owner.prefab";
        private string ConsumerPath => _testRoot + "/Consumer.prefab";
        private string VariantPath => _testRoot + "/Variant.prefab";

        [SetUp]
        public void SetUp()
        {
            _testRoot = "Assets/__SuperHeroUIOverrideRepairTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(_testRoot);
            StyleRecipeProcessor.ClearPreviewCache();
        }

        [Test]
        public void RepairPreviewIsReadOnlyAndDoesNotIncludeOwnerBakeChanges()
        {
            Fixture fixture = CreateFixture();
            GameObject consumer = CreateConsumer(fixture.OwnerPrefab, ConsumerPath, true);
            RegisterConsumers(fixture.Recipe, consumer);
            string[] paths =
            {
                OwnerPath,
                ConsumerPath,
                AssetDatabase.GetAssetPath(fixture.Registry),
                AssetDatabase.GetAssetPath(fixture.Recipe),
                AssetDatabase.GetAssetPath(fixture.Token),
            };
            string[] contents = Array.ConvertAll(paths, File.ReadAllText);

            StyleReview ordinary = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);

            Assert.That(ordinary.State, Is.EqualTo(StyleReviewState.Error), ordinary.ToString());
            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Stale), repairs.ToString());
            Assert.That(repairs.Changes, Is.Not.Empty);
            Assert.That(repairs.Changes, Has.All.Contains(ConsumerPath));
            for (int index = 0; index < paths.Length; index++)
            {
                Assert.That(File.ReadAllText(paths[index]), Is.EqualTo(contents[index]), paths[index]);
            }

            StyleReview after = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(after.State, Is.EqualTo(StyleReviewState.Error), after.ToString());
        }

        [Test]
        public void RepairPreservesUnmanagedOverridesAndNestedInstancesThenApplyIsIdempotent()
        {
            Fixture fixture = CreateFixture();
            GameObject consumer = CreateConsumer(fixture.OwnerPrefab, ConsumerPath, true, 2);
            string unregisteredPath = _testRoot + "/Unregistered.prefab";
            CreateConsumer(fixture.OwnerPrefab, unregisteredPath, true);
            RegisterConsumers(fixture.Recipe, consumer);
            string ownerBefore = File.ReadAllText(OwnerPath);
            string unregisteredBefore = File.ReadAllText(unregisteredPath);

            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            StyleReview repaired = StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs);

            Assert.That(repaired.State, Is.EqualTo(StyleReviewState.Stale), repaired.ToString());
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            Assert.That(File.ReadAllText(unregisteredPath), Is.EqualTo(unregisteredBefore));
            AssertConsumerInstances(Color.white, 2);
            string consumerAfterRepair = File.ReadAllText(ConsumerPath);

            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, repaired);

            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
            AssertConsumerInstances(fixture.Token.Value, 2);
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerAfterRepair));
            string ownerAfterApply = File.ReadAllText(OwnerPath);

            StyleReview repeated = StyleRecipeProcessor.Apply(fixture.Registry, applied);

            Assert.That(repeated.State, Is.EqualTo(StyleReviewState.Ready), repeated.ToString());
            Assert.That(repeated.Changes, Is.Empty);
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerAfterApply));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerAfterRepair));
            Assert.That(File.ReadAllText(unregisteredPath), Is.EqualTo(unregisteredBefore));
            StyleReview emptyRepairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            Assert.That(emptyRepairs.State, Is.EqualTo(StyleReviewState.Ready), emptyRepairs.ToString());
            Assert.That(emptyRepairs.Changes, Is.Empty);
            StyleReview repeatedRepair = StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, emptyRepairs);
            Assert.That(repeatedRepair.State, Is.EqualTo(StyleReviewState.Ready), repeatedRepair.ToString());
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerAfterApply));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerAfterRepair));
            Assert.That(File.ReadAllText(unregisteredPath), Is.EqualTo(unregisteredBefore));
        }

        [Test]
        public void RepairRejectsUnsavedConsumerAssetWithoutWriting()
        {
            Fixture fixture = CreateFixture();
            GameObject consumer = CreateConsumer(fixture.OwnerPrefab, ConsumerPath, true);
            RegisterConsumers(fixture.Recipe, consumer);
            StyleReview approved = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            Assert.That(approved.State, Is.EqualTo(StyleReviewState.Stale), approved.ToString());
            string ownerBefore = File.ReadAllText(OwnerPath);
            string consumerBefore = File.ReadAllText(ConsumerPath);
            consumer.name = "Unsaved consumer name";
            EditorUtility.SetDirty(consumer);

            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);

            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Error), repairs.ToString());
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, approved));
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs));
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerBefore));
            Assert.That(consumer.name, Is.EqualTo("Unsaved consumer name"));
            Assert.That(EditorUtility.IsDirty(consumer), Is.True);
        }

        [Test]
        public void RepairRestoresSelectableAndTextPropertiesButPreservesContentAndEvents()
        {
            Fixture fixture = CreateFixture();
            AddStyledButtonAndLabel(fixture);
            BakeOwner(fixture);
            GameObject consumer = CreateConsumer(fixture.OwnerPrefab, ConsumerPath, false);
            using (LoadedPrefab loaded = LoadedPrefab.Open(ConsumerPath))
            {
                GameObject instance = loaded.Root.transform.GetChild(0).gameObject;
                Button button = instance.GetComponent<Button>();
                using (var serializedButton = new SerializedObject(button))
                {
                    serializedButton.FindProperty("m_Colors.m_NormalColor.r").floatValue = 0.91f;
                    serializedButton.ApplyModifiedPropertiesWithoutUndo();
                }

                button.interactable = false;
                UnityEventTools.AddBoolPersistentListener(button.onClick, instance.SetActive, false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                TMP_Text label = instance.GetComponentInChildren<TMP_Text>();
                label.fontSize = 42f;
                label.text = "Project-owned consumer text";
                PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                PrefabUtility.SaveAsPrefabAsset(loaded.Root, ConsumerPath);
            }

            RegisterConsumers(fixture.Recipe, consumer);
            string ownerBefore = File.ReadAllText(OwnerPath);
            StyleReview ordinary = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(ordinary.State, Is.EqualTo(StyleReviewState.Error), ordinary.ToString());
            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Stale), repairs.ToString());

            StyleReview repaired = StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs);

            Assert.That(repaired.State, Is.EqualTo(StyleReviewState.Ready), repaired.ToString());
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            using (LoadedPrefab loaded = LoadedPrefab.Open(ConsumerPath))
            {
                GameObject instance = loaded.Root.transform.GetChild(0).gameObject;
                Button button = instance.GetComponent<Button>();
                Assert.That(button.colors.normalColor, Is.EqualTo(fixture.Token.Value));
                Assert.That(button.interactable, Is.False);
                Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
                Assert.That(button.onClick.GetPersistentTarget(0), Is.EqualTo(instance));
                Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo("SetActive"));
                TMP_Text label = instance.GetComponentInChildren<TMP_Text>();
                Assert.That(label.fontSize, Is.EqualTo(24f));
                Assert.That(label.text, Is.EqualTo("Project-owned consumer text"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RepairRejectsDependencyChangesAfterReviewWithoutWriting(bool changeConsumer)
        {
            Fixture fixture = CreateFixture();
            GameObject consumer = CreateConsumer(fixture.OwnerPrefab, ConsumerPath, true);
            RegisterConsumers(fixture.Recipe, consumer);
            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Stale), repairs.ToString());

            if (changeConsumer)
            {
                using (LoadedPrefab loaded = LoadedPrefab.Open(ConsumerPath))
                {
                    Image image = loaded.Root.transform.GetChild(0).GetComponent<Image>();
                    image.color = Color.magenta;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                    PrefabUtility.SaveAsPrefabAsset(loaded.Root, ConsumerPath);
                }
            }
            else
            {
                SetPrivateField(fixture.Token, "_value", Color.green);
                EditorUtility.SetDirty(fixture.Token);
                AssetDatabase.SaveAssets();
            }

            string ownerBeforeApply = File.ReadAllText(OwnerPath);
            string consumerBeforeApply = File.ReadAllText(ConsumerPath);

            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs));

            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBeforeApply));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerBeforeApply));
        }

        [Test]
        public void OrdinaryAndRepairReviewsCannotAuthorizeTheOtherApplyOperation()
        {
            Fixture fixture = CreateFixture();
            GameObject consumer = CreateConsumer(fixture.OwnerPrefab, ConsumerPath, true);
            RegisterConsumers(fixture.Recipe, consumer);
            StyleReview ordinary = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            string ownerBefore = File.ReadAllText(OwnerPath);
            string consumerBefore = File.ReadAllText(ConsumerPath);

            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, ordinary));
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.Apply(fixture.Registry, repairs));

            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerBefore));
        }

        [Test]
        public void OtherValidationErrorsPreventRepairWithoutWriting()
        {
            Fixture fixture = CreateFixture();
            GameObject consumer = CreateConsumer(fixture.OwnerPrefab, ConsumerPath, true);
            RegisterConsumers(fixture.Recipe, consumer);
            SetPrivateField(fixture.Recipe.GraphicColors[0].Target, "_globalObjectId", string.Empty);
            EditorUtility.SetDirty(fixture.Recipe);
            AssetDatabase.SaveAssets();
            string ownerBefore = File.ReadAllText(OwnerPath);
            string consumerBefore = File.ReadAllText(ConsumerPath);

            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);

            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Error), repairs.ToString());
            Assert.Throws<InvalidOperationException>(
                () => StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs));
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerBefore));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IntermediateVariantMustBeExplicitlyRegisteredForRepair(bool registerVariant)
        {
            Fixture fixture = CreateFixture();
            BakeOwner(fixture);
            GameObject variant = CreateVariant(fixture.OwnerPrefab, Color.black);
            GameObject consumer = CreateConsumer(variant, ConsumerPath, false);
            RegisterConsumers(
                fixture.Recipe,
                registerVariant ? new[] { variant, consumer } : new[] { consumer });
            string ownerBefore = File.ReadAllText(OwnerPath);
            string variantBefore = File.ReadAllText(VariantPath);
            string consumerBefore = File.ReadAllText(ConsumerPath);

            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);

            if (!registerVariant)
            {
                Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Error), repairs.ToString());
                Assert.Throws<InvalidOperationException>(
                    () => StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs));
                Assert.That(File.ReadAllText(VariantPath), Is.EqualTo(variantBefore));
            }
            else
            {
                Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Stale), repairs.ToString());
                StyleReview repaired = StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs);
                Assert.That(repaired.State, Is.EqualTo(StyleReviewState.Ready), repaired.ToString());
                using (LoadedPrefab loaded = LoadedPrefab.Open(VariantPath))
                {
                    Assert.That(loaded.Root.GetComponent<Image>().color, Is.EqualTo(fixture.Token.Value));
                }
            }

            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerBefore));
        }

        [Test]
        public void RepairPreservesExplicitBaseRecipeSpecialization()
        {
            Fixture fixture = CreateFixture();
            BakeOwner(fixture);
            GameObject variant = CreateVariant(fixture.OwnerPrefab, Color.yellow);
            ColorToken specializationToken = CreateAsset<ColorToken>("Specialization Color.asset");
            SetPrivateField(specializationToken, "_value", Color.yellow);
            EditorUtility.SetDirty(specializationToken);
            PrefabStyleRecipe specialization = CreateRecipe(
                "Specialization Recipe.asset", variant, specializationToken);
            SetPrivateField(specialization, "_baseRecipe", fixture.Recipe);
            GameObject consumer = CreateConsumer(variant, ConsumerPath, true);
            RegisterConsumers(fixture.Recipe, variant);
            RegisterConsumers(specialization, consumer);
            SetPrivateField(fixture.Registry, "_recipes", new[] { fixture.Recipe, specialization });
            EditorUtility.SetDirty(fixture.Registry);
            AssetDatabase.SaveAssets();
            string ownerBefore = File.ReadAllText(OwnerPath);
            string variantBefore = File.ReadAllText(VariantPath);

            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);
            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Stale), repairs.ToString());
            StyleReview repaired = StyleRecipeProcessor.ApplyOverrideRepairs(fixture.Registry, repairs);

            Assert.That(repaired.State, Is.EqualTo(StyleReviewState.Ready), repaired.ToString());
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            Assert.That(File.ReadAllText(VariantPath), Is.EqualTo(variantBefore));
            using (LoadedPrefab loaded = LoadedPrefab.Open(ConsumerPath))
            {
                Assert.That(
                    loaded.Root.transform.GetChild(0).GetComponent<Image>().color,
                    Is.EqualTo(Color.yellow));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChainedSpecializationsAreReadyInEveryRegisteredAncestorConsumer(bool explicitBaseRecipes)
        {
            Fixture fixture = CreateFixture();
            BakeOwner(fixture);
            GameObject variant = CreateVariant(fixture.OwnerPrefab, Color.yellow);
            PrefabStyleRecipe variantRecipe = CreateSpecialization(
                "Variant", variant, Color.yellow, explicitBaseRecipes ? fixture.Recipe : null);
            RegisterConsumers(fixture.Recipe, variant);
            SetPrivateField(fixture.Registry, "_recipes", new[] { fixture.Recipe, variantRecipe });
            EditorUtility.SetDirty(fixture.Registry);
            AssetDatabase.SaveAssets();
            StyleReview firstSpecialization = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(firstSpecialization.State, Is.EqualTo(StyleReviewState.Ready), firstSpecialization.ToString());

            string derivedPath = _testRoot + "/Derived Variant.prefab";
            GameObject derived = CreateVariant(variant, Color.green, derivedPath);
            PrefabStyleRecipe derivedRecipe = CreateSpecialization(
                "Derived", derived, Color.green, explicitBaseRecipes ? variantRecipe : null);
            GameObject consumer = CreateConsumer(derived, ConsumerPath, false);
            RegisterConsumers(fixture.Recipe, variant, derived, consumer);
            RegisterConsumers(variantRecipe, derived, consumer);
            RegisterConsumers(derivedRecipe, consumer);
            SetPrivateField(fixture.Registry, "_recipes", new[] { fixture.Recipe, variantRecipe, derivedRecipe });
            EditorUtility.SetDirty(fixture.Registry);
            AssetDatabase.SaveAssets();
            string[] paths =
            {
                OwnerPath, VariantPath, derivedPath, ConsumerPath,
                AssetDatabase.GetAssetPath(variantRecipe), AssetDatabase.GetAssetPath(derivedRecipe)
            };
            string[] contents = Array.ConvertAll(paths, File.ReadAllText);

            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);

            Assert.That(preview.State, Is.EqualTo(StyleReviewState.Ready), preview.ToString());
            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Ready), repairs.ToString());
            Assert.That(repairs.Changes, Is.Empty);
            for (int index = 0; index < paths.Length; index++)
            {
                Assert.That(File.ReadAllText(paths[index]), Is.EqualTo(contents[index]), paths[index]);
            }
        }

        [Test]
        public void VariantRecipeWithoutExplicitBaseRecipeAutomaticallyPreservesSpecializedValues()
        {
            Fixture fixture = CreateFixture();
            BakeOwner(fixture);
            GameObject variant = CreateVariant(fixture.OwnerPrefab, Color.yellow);
            PrefabStyleRecipe variantRecipe = CreateSpecialization("Variant", variant, Color.yellow, null);
            GameObject consumer = CreateConsumer(variant, ConsumerPath, false);
            RegisterConsumers(fixture.Recipe, variant, consumer);
            RegisterConsumers(variantRecipe, consumer);
            SetPrivateField(fixture.Registry, "_recipes", new[] { fixture.Recipe, variantRecipe });
            EditorUtility.SetDirty(fixture.Registry);
            AssetDatabase.SaveAssets();
            string ownerBefore = File.ReadAllText(OwnerPath);
            string variantBefore = File.ReadAllText(VariantPath);
            string consumerBefore = File.ReadAllText(ConsumerPath);
            string recipeBefore = File.ReadAllText(AssetDatabase.GetAssetPath(variantRecipe));

            StyleReview ordinary = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview repairs = StyleRecipeProcessor.PreviewOverrideRepairs(fixture.Registry);

            Assert.That(ordinary.State, Is.EqualTo(StyleReviewState.Ready), ordinary.ToString());
            Assert.That(repairs.State, Is.EqualTo(StyleReviewState.Ready), repairs.ToString());
            Assert.That(repairs.Changes, Is.Empty);
            Assert.That(variantRecipe.BaseRecipe, Is.Null);
            Assert.That(File.ReadAllText(AssetDatabase.GetAssetPath(variantRecipe)), Is.EqualTo(recipeBefore));
            Assert.That(File.ReadAllText(OwnerPath), Is.EqualTo(ownerBefore));
            Assert.That(File.ReadAllText(VariantPath), Is.EqualTo(variantBefore));
            Assert.That(File.ReadAllText(ConsumerPath), Is.EqualTo(consumerBefore));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CachedAncestorConsumerRechecksUnmarkedDescendantRecipeChanges(bool changeOwnership)
        {
            Fixture fixture = CreateFixture();
            using (LoadedPrefab owner = LoadedPrefab.Open(OwnerPath))
            {
                var decoration = new GameObject("Decoration", typeof(RectTransform), typeof(Image));
                decoration.transform.SetParent(owner.Root.transform, false);
                PrefabUtility.SaveAsPrefabAsset(owner.Root, OwnerPath);
            }

            BakeOwner(fixture);
            GameObject variant = CreateVariant(fixture.OwnerPrefab, Color.yellow);
            PrefabStyleRecipe variantRecipe = CreateSpecialization("Variant", variant, Color.yellow, null);
            string derivedPath = _testRoot + "/Derived Variant.prefab";
            GameObject derived = CreateVariant(variant, Color.green, derivedPath);
            PrefabStyleRecipe derivedRecipe = CreateSpecialization("Derived", derived, Color.green, variantRecipe);
            GameObject consumer = CreateConsumer(derived, ConsumerPath, false);
            RegisterConsumers(fixture.Recipe, consumer);
            SetPrivateField(fixture.Registry, "_recipes", new[] { fixture.Recipe, variantRecipe, derivedRecipe });
            EditorUtility.SetDirty(fixture.Registry);
            ColorToken decorationToken = CreateAsset<ColorToken>("Decoration Color.asset");
            SetPrivateField(decorationToken, "_value", Color.white);
            EditorUtility.SetDirty(decorationToken);
            AssetDatabase.SaveAssets();

            var decorationTarget = new PrefabTargetReference();
            using (LoadedPrefab loaded = LoadedPrefab.Open(derivedPath))
            {
                Image decoration = loaded.Root.transform.GetChild(0).GetComponent<Image>();
                decorationTarget.Configure(
                    GlobalObjectId.GetGlobalObjectIdSlow(decoration).ToString(),
                    PrefabTargetResolver.GetDisplayPath(loaded.Root.transform, decoration.transform),
                    typeof(Image).FullName,
                    PrefabTargetKind.Graphic);
            }

            var decorationBinding = new PrefabStyleRecipe.GraphicColorBinding();
            SetPrivateField(decorationBinding, "_target", decorationTarget);
            SetPrivateField(decorationBinding, "_color", decorationToken);
            StyleReview ready = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(ready.State, Is.EqualTo(StyleReviewState.Ready), ready.ToString());
            StyleReview warm = StyleRecipeProcessor.Preview(fixture.Registry);
            Assert.That(warm.ConsumerPrefabLoadCount, Is.Zero);

            if (changeOwnership)
            {
                SetPrivateField(derivedRecipe, "_graphicColors", new[] { decorationBinding });
            }
            else
            {
                SetPrivateField(derivedRecipe, "_baseRecipe", null);
            }

            Assert.That(EditorUtility.IsDirty(derivedRecipe), Is.False);
            StyleReview cached = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview full = StyleRecipeProcessor.PreviewFull(fixture.Registry);

            StyleReviewState expected = changeOwnership ? StyleReviewState.Error : StyleReviewState.Ready;
            Assert.That(full.State, Is.EqualTo(expected), full.ToString());
            Assert.That(cached.State, Is.EqualTo(expected), cached.ToString());
            Assert.That(cached.Errors, Is.EqualTo(full.Errors));
            Assert.That(cached.ConsumerPrefabLoadCount, Is.EqualTo(1));
        }

        private Fixture CreateFixture()
        {
            var owner = new GameObject("Owner", typeof(RectTransform), typeof(Image));
            owner.GetComponent<Image>().color = Color.white;
            PrefabUtility.SaveAsPrefabAsset(owner, OwnerPath);
            UnityEngine.Object.DestroyImmediate(owner);
            GameObject ownerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OwnerPath);
            ColorToken token = CreateAsset<ColorToken>("Color.asset");
            SetPrivateField(token, "_value", new Color(0.12f, 0.24f, 0.36f, 1f));
            EditorUtility.SetDirty(token);
            PrefabStyleRecipe recipe = CreateRecipe("Recipe.asset", ownerPrefab, token);
            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry.asset");
            SetPrivateField(registry, "_recipes", new[] { recipe });
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return new Fixture(registry, recipe, ownerPrefab, token);
        }

        private PrefabStyleRecipe CreateRecipe(string fileName, GameObject ownerPrefab, ColorToken token)
        {
            var target = new PrefabTargetReference();
            using (LoadedPrefab owner = LoadedPrefab.Open(AssetDatabase.GetAssetPath(ownerPrefab)))
            {
                Image image = owner.Root.GetComponent<Image>();
                target.Configure(
                    GlobalObjectId.GetGlobalObjectIdSlow(image).ToString(),
                    PrefabTargetResolver.GetDisplayPath(owner.Root.transform, image.transform),
                    typeof(Image).FullName,
                    PrefabTargetKind.Graphic);
            }

            var binding = new PrefabStyleRecipe.GraphicColorBinding();
            SetPrivateField(binding, "_target", target);
            SetPrivateField(binding, "_color", token);
            PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>(fileName);
            SetPrivateField(recipe, "_ownerPrefab", ownerPrefab);
            SetPrivateField(recipe, "_graphicColors", new[] { binding });
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private PrefabStyleRecipe CreateSpecialization(
            string name,
            GameObject ownerPrefab,
            Color color,
            PrefabStyleRecipe baseRecipe)
        {
            ColorToken token = CreateAsset<ColorToken>(name + " Color.asset");
            SetPrivateField(token, "_value", color);
            EditorUtility.SetDirty(token);
            PrefabStyleRecipe recipe = CreateRecipe(name + " Recipe.asset", ownerPrefab, token);
            SetPrivateField(recipe, "_baseRecipe", baseRecipe);
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private static void BakeOwner(Fixture fixture)
        {
            StyleReview preview = StyleRecipeProcessor.Preview(fixture.Registry);
            StyleReview applied = StyleRecipeProcessor.Apply(fixture.Registry, preview);
            Assert.That(applied.State, Is.EqualTo(StyleReviewState.Ready), applied.ToString());
        }

        private void AddStyledButtonAndLabel(Fixture fixture)
        {
            using (LoadedPrefab owner = LoadedPrefab.Open(OwnerPath))
            {
                owner.Root.AddComponent<Button>().targetGraphic = owner.Root.GetComponent<Image>();
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(owner.Root.transform, false);
                label.GetComponent<TMP_Text>().text = "Owner text";
                PrefabUtility.SaveAsPrefabAsset(owner.Root, OwnerPath);
            }

            var buttonTarget = new PrefabTargetReference();
            var textTarget = new PrefabTargetReference();
            TMP_FontAsset font;
            using (LoadedPrefab owner = LoadedPrefab.Open(OwnerPath))
            {
                Button button = owner.Root.GetComponent<Button>();
                buttonTarget.Configure(
                    GlobalObjectId.GetGlobalObjectIdSlow(button).ToString(),
                    PrefabTargetResolver.GetDisplayPath(owner.Root.transform, button.transform),
                    typeof(Button).FullName,
                    PrefabTargetKind.Selectable);
                TMP_Text label = owner.Root.GetComponentInChildren<TMP_Text>();
                font = label.font;
                textTarget.Configure(
                    GlobalObjectId.GetGlobalObjectIdSlow(label).ToString(),
                    PrefabTargetResolver.GetDisplayPath(owner.Root.transform, label.transform),
                    label.GetType().FullName,
                    PrefabTargetKind.Text);
            }

            Assert.That(font, Is.Not.Null, "TextMesh Pro default font is unavailable.");
            SelectableStyle buttonStyle = CreateAsset<SelectableStyle>("Button Style.asset");
            SetPrivateField(buttonStyle, "_normal", fixture.Token);
            SetPrivateField(buttonStyle, "_highlighted", fixture.Token);
            SetPrivateField(buttonStyle, "_pressed", fixture.Token);
            SetPrivateField(buttonStyle, "_selected", fixture.Token);
            SetPrivateField(buttonStyle, "_disabled", fixture.Token);
            EditorUtility.SetDirty(buttonStyle);
            var buttonBinding = new PrefabStyleRecipe.SelectableBinding();
            SetPrivateField(buttonBinding, "_target", buttonTarget);
            SetPrivateField(buttonBinding, "_style", buttonStyle);
            SetPrivateField(fixture.Recipe, "_selectables", new[] { buttonBinding });
            TextStyle textStyle = CreateAsset<TextStyle>("Text Style.asset");
            SetPrivateField(textStyle, "_font", font);
            SetPrivateField(textStyle, "_color", fixture.Token);
            SetPrivateField(textStyle, "_fontSize", 24f);
            EditorUtility.SetDirty(textStyle);
            var textBinding = new PrefabStyleRecipe.TextBinding();
            SetPrivateField(textBinding, "_target", textTarget);
            SetPrivateField(textBinding, "_style", textStyle);
            SetPrivateField(fixture.Recipe, "_texts", new[] { textBinding });
            EditorUtility.SetDirty(fixture.Recipe);
            AssetDatabase.SaveAssets();
        }

        private GameObject CreateVariant(GameObject ownerPrefab, Color color, string path = null)
        {
            path ??= VariantPath;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(ownerPrefab);
            Image image = instance.GetComponent<Image>();
            image.color = color;
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static GameObject CreateConsumer(
            GameObject prefab,
            string path,
            bool overrideColor,
            int instanceCount = 1)
        {
            var root = new GameObject("Consumer", typeof(RectTransform));
            for (int index = 0; index < instanceCount; index++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(root.transform, false);
                Image image = instance.GetComponent<Image>();
                if (overrideColor)
                {
                    image.color = Color.black;
                }

                image.raycastTarget = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(image);
                var rectangle = (RectTransform)instance.transform;
                rectangle.anchoredPosition = new Vector2(17f + index, 23f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rectangle);
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private void AssertConsumerInstances(Color expectedColor, int expectedCount)
        {
            using (LoadedPrefab loaded = LoadedPrefab.Open(ConsumerPath))
            {
                Assert.That(loaded.Root.transform.childCount, Is.EqualTo(expectedCount));
                for (int index = 0; index < expectedCount; index++)
                {
                    GameObject instance = loaded.Root.transform.GetChild(index).gameObject;
                    Image image = instance.GetComponent<Image>();
                    Assert.That(image.color, Is.EqualTo(expectedColor));
                    Assert.That(image.raycastTarget, Is.False);
                    var serializedImage = new SerializedObject(image);
                    Assert.That(serializedImage.FindProperty("m_RaycastTarget").prefabOverride, Is.True);
                    Assert.That(serializedImage.FindProperty("m_Color").prefabOverride, Is.False);
                    Assert.That(
                        ((RectTransform)instance.transform).anchoredPosition,
                        Is.EqualTo(new Vector2(17f + index, 23f)));
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance), Is.EqualTo(OwnerPath));
                }
            }
        }

        private static void RegisterConsumers(PrefabStyleRecipe recipe, params GameObject[] consumers)
        {
            SetPrivateField(recipe, "_consumerPrefabs", consumers);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssets();
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
        }

        private sealed class Fixture
        {
            public StyleRecipeRegistry Registry { get; }
            public PrefabStyleRecipe Recipe { get; }
            public GameObject OwnerPrefab { get; }
            public ColorToken Token { get; }

            public Fixture(
                StyleRecipeRegistry registry,
                PrefabStyleRecipe recipe,
                GameObject ownerPrefab,
                ColorToken token)
            {
                Registry = registry;
                Recipe = recipe;
                OwnerPrefab = ownerPrefab;
                Token = token;
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
