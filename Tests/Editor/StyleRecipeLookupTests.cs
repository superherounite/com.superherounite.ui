using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRecipeLookupTests
    {
        private string _testRoot;
        private Scene _testScene;
        private Scene _previousActiveScene;
        private Object[] _previousSelection;

        [SetUp]
        public void SetUp()
        {
            _previousSelection = Selection.objects;
            _previousActiveScene = SceneManager.GetActiveScene();
            _testRoot = "Assets/__SuperHeroUIRecipeLookupTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
            _testScene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath.StartsWith(_testRoot + "/", StringComparison.Ordinal))
            {
                stage.ClearDirtiness();
                StageUtility.GoToMainStage();
            }

            if (_previousActiveScene.IsValid() && _previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(_previousActiveScene);
            }

            if (_testScene.IsValid() && _testScene.isLoaded)
            {
                EditorSceneManager.ClosePreviewScene(_testScene);
            }

            Selection.objects = _previousSelection.Where(target => target != null).ToArray();
            if (!string.IsNullOrEmpty(_testRoot))
            {
                AssetDatabase.DeleteAsset(_testRoot);
            }

            _testRoot = null;
        }

        [Test]
        public void DefaultLookupUsesOwnerReferenceRegardlessOfNamesAndDoesNotWrite()
        {
            GameObject prefab = CreatePrefab("Zone Danger Button.prefab");
            PrefabStyleRecipe recipe = CreateRecipe("Unrelated Archive 42.asset", prefab);
            var lookup = new StyleRecipeLookup();
            IReadOnlyDictionary<string, byte[]> before = CaptureFiles();
            int dirtyCount = EditorUtility.GetDirtyCount(recipe);
            bool sceneDirty = _testScene.isDirty;
            Object[] selection = Selection.objects;

            IReadOnlyList<StyleRecipeMatch> matches = lookup.Find(prefab);

            AssertSingleMatch(matches, recipe, StyleRecipeRelationship.Owner, prefab);
            Assert.That(EditorUtility.GetDirtyCount(recipe), Is.EqualTo(dirtyCount));
            Assert.That(_testScene.isDirty, Is.EqualTo(sceneDirty));
            Assert.That(Selection.objects, Is.EqualTo(selection));
            AssertFilesUnchanged(before);
        }

        [Test]
        public void DefaultLookupFindsRecipeSubassetsByTheirOwnOwnerReference()
        {
            GameObject mainOwner = CreatePrefab("First Owner.prefab");
            GameObject subassetOwner = CreatePrefab("Second Owner.prefab");
            PrefabStyleRecipe mainRecipe = CreateRecipe("Unrelated Shared Archive.asset", mainOwner);
            string sharedPath = AssetDatabase.GetAssetPath(mainRecipe);
            var subassetRecipe = ScriptableObject.CreateInstance<PrefabStyleRecipe>();
            subassetRecipe.name = "Unrelated Embedded Entry";
            SetPrivateField(subassetRecipe, "_ownerPrefab", subassetOwner);
            AssetDatabase.AddObjectToAsset(subassetRecipe, mainRecipe);
            EditorUtility.SetDirty(subassetRecipe);
            AssetDatabase.SaveAssetIfDirty(subassetRecipe);
            AssetDatabase.ImportAsset(sharedPath, ImportAssetOptions.ForceSynchronousImport);
            PrefabStyleRecipe savedSubasset = AssetDatabase.LoadAllAssetsAtPath(sharedPath)
                .OfType<PrefabStyleRecipe>()
                .Single(recipe => recipe.name == "Unrelated Embedded Entry");
            Assert.That(AssetDatabase.IsSubAsset(savedSubasset), Is.True);
            var lookup = new StyleRecipeLookup();
            IReadOnlyDictionary<string, byte[]> before = CaptureFiles();

            IReadOnlyList<StyleRecipeMatch> matches = lookup.Find(subassetOwner);

            AssertSingleMatch(matches, savedSubasset, StyleRecipeRelationship.Owner, subassetOwner);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void ProjectPrefabChildAndComponentResolveTheirOwner()
        {
            GameObject prefab = CreatePrefab("Owner.prefab");
            PrefabStyleRecipe recipe = CreateRecipe("Recipe.asset", prefab);
            StyleRecipeLookup lookup = CreateLookup(recipe);
            Transform child = prefab.transform.Find("Label");

            AssertSingleMatch(lookup.Find(child.gameObject), recipe, StyleRecipeRelationship.Owner, prefab);
            AssertSingleMatch(lookup.Find(child), recipe, StyleRecipeRelationship.Owner, prefab);
        }

        [Test]
        public void ScenePrefabInstanceAndAddedChildResolveTheirOwner()
        {
            GameObject prefab = CreatePrefab("Owner.prefab");
            PrefabStyleRecipe recipe = CreateRecipe("Recipe.asset", prefab);
            StyleRecipeLookup lookup = CreateLookup(recipe);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _testScene);
            var added = new GameObject("Unsaved Added Label");
            added.transform.SetParent(instance.transform.Find("Label"), false);

            Assert.That(StyleRecipeLookup.HasPrefabContext(added), Is.True);
            AssertSingleMatch(lookup.Find(instance), recipe, StyleRecipeRelationship.Owner, prefab);
            AssertSingleMatch(lookup.Find(instance.transform.Find("Label")), recipe, StyleRecipeRelationship.Owner, prefab);
            AssertSingleMatch(lookup.Find(added), recipe, StyleRecipeRelationship.Owner, prefab);
        }

        [Test]
        public void NestedSceneInstanceAndAddedChildUseNearestPrefabOwner()
        {
            GameObject nested = CreatePrefab("Nested.prefab");
            GameObject outer = CreatePrefab("Outer.prefab", nested);
            PrefabStyleRecipe nestedRecipe = CreateRecipe("Nested Recipe.asset", nested);
            PrefabStyleRecipe outerRecipe = CreateRecipe("Outer Recipe.asset", outer);
            StyleRecipeLookup lookup = CreateLookup(outerRecipe, nestedRecipe);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(outer, _testScene);
            Transform nestedInstance = instance.transform.Find("Nested");
            var added = new GameObject("Added Inside Nested Prefab");
            added.transform.SetParent(nestedInstance.Find("Label"), false);

            Assert.That(StyleRecipeLookup.HasPrefabContext(added), Is.True);
            AssertSingleMatch(lookup.Find(nestedInstance), nestedRecipe, StyleRecipeRelationship.Owner, nested);
            AssertSingleMatch(lookup.Find(nestedInstance.Find("Label")), nestedRecipe, StyleRecipeRelationship.Owner, nested);
            AssertSingleMatch(lookup.Find(added), nestedRecipe, StyleRecipeRelationship.Owner, nested);
        }

        [Test]
        public void ProjectNestedPrefabSelectionUsesNearestPrefabOwner()
        {
            GameObject nested = CreatePrefab("Nested.prefab");
            GameObject outer = CreatePrefab("Outer.prefab", nested);
            PrefabStyleRecipe nestedRecipe = CreateRecipe("Nested Recipe.asset", nested);
            PrefabStyleRecipe outerRecipe = CreateRecipe("Outer Recipe.asset", outer);
            StyleRecipeLookup lookup = CreateLookup(outerRecipe, nestedRecipe);

            AssertSingleMatch(lookup.Find(outer.transform.Find("Nested/Label")),
                nestedRecipe, StyleRecipeRelationship.Owner, nested);
            AssertSingleMatch(lookup.Find(outer), outerRecipe, StyleRecipeRelationship.Owner, outer);
        }

        [Test]
        public void VariantFindsDirectOwnerAndInheritedBaseRecipe()
        {
            Assert.That(PrefabStageUtility.GetCurrentPrefabStage(), Is.Null,
                "This test must not replace a Prefab Stage opened by another test or the user.");
            GameObject basePrefab = CreatePrefab("Base.prefab");
            GameObject variant = CreateVariant("Variant.prefab", basePrefab);
            PrefabStyleRecipe baseRecipe = CreateRecipe("A Base Recipe.asset", basePrefab);
            PrefabStyleRecipe variantRecipe = CreateRecipe("Z Variant Recipe.asset", variant);
            string basePath = AssetDatabase.GetAssetPath(basePrefab);
            string variantPath = AssetDatabase.GetAssetPath(variant);
            string baseRecipePath = AssetDatabase.GetAssetPath(baseRecipe);
            string variantRecipePath = AssetDatabase.GetAssetPath(variantRecipe);
            StyleRecipeLookup lookup = CreateLookup(baseRecipe, variantRecipe);

            IReadOnlyList<StyleRecipeMatch> matches = lookup.Find(variant.transform.Find("Label"));

            Assert.That(matches.Count, Is.EqualTo(2));
            AssertMatch(matches[0], variantRecipe, StyleRecipeRelationship.Owner, variant);
            AssertMatch(matches[1], baseRecipe, StyleRecipeRelationship.Inherited, basePrefab);

            PrefabStage stage = PrefabStageUtility.OpenPrefab(variantPath);
            Assert.That(stage, Is.Not.Null);
            // Prefab Mode may unload assets held only by the managed fixture stack.
            basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            variant = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            baseRecipe = AssetDatabase.LoadAssetAtPath<PrefabStyleRecipe>(baseRecipePath);
            variantRecipe = AssetDatabase.LoadAssetAtPath<PrefabStyleRecipe>(variantRecipePath);
            lookup = CreateLookup(baseRecipe, variantRecipe);
            IReadOnlyDictionary<string, byte[]> before = CaptureFiles();
            bool dirty = stage.scene.isDirty;
            Object[] stageTargets =
            {
                stage.prefabContentsRoot,
                stage.prefabContentsRoot.transform.Find("Label")
            };
            foreach (Object target in stageTargets)
            {
                IReadOnlyList<StyleRecipeMatch> stageMatches = lookup.Find(target);
                Assert.That(stageMatches.Count, Is.EqualTo(2));
                AssertMatch(stageMatches[0], variantRecipe, StyleRecipeRelationship.Owner, variant);
                AssertMatch(stageMatches[1], baseRecipe, StyleRecipeRelationship.Inherited, basePrefab);
            }

            Assert.That(stage.scene.isDirty, Is.EqualTo(dirty));
            AssertFilesUnchanged(before);
        }

        [Test]
        public void ConsumerLookupRequiresAnExplicitRecipeReference()
        {
            GameObject owner = CreatePrefab("Owner.prefab");
            GameObject consumer = CreatePrefab("Registered Consumer.prefab", owner);
            GameObject unrelatedContainer = CreatePrefab("Unregistered Consumer.prefab", owner);
            PrefabStyleRecipe recipe = CreateRecipe("Recipe.asset", owner, consumer);
            StyleRecipeLookup lookup = CreateLookup(recipe);

            AssertSingleMatch(lookup.Find(consumer), recipe, StyleRecipeRelationship.Consumer, consumer);
            AssertSingleMatch(lookup.Find(consumer.transform.Find("Label")), recipe, StyleRecipeRelationship.Consumer, consumer);
            Assert.That(lookup.Find(unrelatedContainer), Is.Empty);
        }

        [Test]
        public void PrefabStageRootChildrenAndNestedChildrenResolveWithoutWrites()
        {
            Assert.That(PrefabStageUtility.GetCurrentPrefabStage(), Is.Null,
                "This test must not replace a Prefab Stage opened by another test or the user.");
            GameObject nested = CreatePrefab("Nested.prefab");
            GameObject outer = CreatePrefab("Outer.prefab", nested);
            string outerPath = AssetDatabase.GetAssetPath(outer);
            string nestedPath = AssetDatabase.GetAssetPath(nested);
            string outerRecipePath = AssetDatabase.GetAssetPath(CreateRecipe("Outer Recipe.asset", outer));
            string nestedRecipePath = AssetDatabase.GetAssetPath(CreateRecipe("Nested Recipe.asset", nested));
            PrefabStage stage = PrefabStageUtility.OpenPrefab(outerPath);
            Assert.That(stage, Is.Not.Null);
            var lookup = new StyleRecipeLookup();
            IReadOnlyDictionary<string, byte[]> before = CaptureFiles();
            bool dirty = stage.scene.isDirty;

            IReadOnlyList<StyleRecipeMatch> rootMatches = lookup.Find(stage.prefabContentsRoot);
            IReadOnlyList<StyleRecipeMatch> childMatches = lookup.Find(stage.prefabContentsRoot.transform.Find("Label"));
            IReadOnlyList<StyleRecipeMatch> nestedMatches = lookup.Find(stage.prefabContentsRoot.transform.Find("Nested/Label"));

            Assert.That(rootMatches.Select(match => AssetDatabase.GetAssetPath(match.Recipe)), Is.EqualTo(new[] { outerRecipePath }));
            Assert.That(rootMatches[0].PrefabPath, Is.EqualTo(outerPath));
            Assert.That(childMatches.Select(match => AssetDatabase.GetAssetPath(match.Recipe)), Is.EqualTo(new[] { outerRecipePath }));
            Assert.That(nestedMatches.Select(match => AssetDatabase.GetAssetPath(match.Recipe)), Is.EqualTo(new[] { nestedRecipePath }));
            Assert.That(nestedMatches[0].PrefabPath, Is.EqualTo(nestedPath));
            Assert.That(stage.scene.isDirty, Is.EqualTo(dirty));
            AssertFilesUnchanged(before);
        }

        [Test]
        public void CatalogIsLoadedOnceUntilExplicitlyInvalidated()
        {
            GameObject firstPrefab = CreatePrefab("First.prefab");
            GameObject secondPrefab = CreatePrefab("Second.prefab");
            PrefabStyleRecipe firstRecipe = CreateRecipe("First Recipe.asset", firstPrefab);
            PrefabStyleRecipe secondRecipe = CreateRecipe("Second Recipe.asset", secondPrefab);
            var catalog = new List<PrefabStyleRecipe> { firstRecipe };
            int loads = 0;
            var lookup = new StyleRecipeLookup(() =>
            {
                loads++;
                return catalog;
            });

            AssertSingleMatch(lookup.Find(firstPrefab), firstRecipe, StyleRecipeRelationship.Owner, firstPrefab);
            catalog.Add(secondRecipe);
            Assert.That(lookup.Find(secondPrefab), Is.Empty);
            Assert.That(loads, Is.EqualTo(1));

            lookup.Invalidate();

            AssertSingleMatch(lookup.Find(secondPrefab), secondRecipe, StyleRecipeRelationship.Owner, secondPrefab);
            Assert.That(loads, Is.EqualTo(2));
        }

        [Test]
        public void CachedCatalogObservesUnsavedOwnerAndConsumerReferenceChanges()
        {
            GameObject original = CreatePrefab("Original.prefab");
            GameObject replacement = CreatePrefab("Replacement.prefab");
            GameObject consumer = CreatePrefab("Consumer.prefab");
            PrefabStyleRecipe recipe = CreateRecipe("Recipe.asset", original);
            StyleRecipeLookup lookup = CreateLookup(recipe);
            AssertSingleMatch(lookup.Find(original), recipe, StyleRecipeRelationship.Owner, original);
            IReadOnlyDictionary<string, byte[]> before = CaptureFiles();
            int dirtyCount = EditorUtility.GetDirtyCount(recipe);

            SetPrivateField(recipe, "_ownerPrefab", replacement);
            SetPrivateField(recipe, "_consumerPrefabs", new[] { consumer });

            Assert.That(lookup.Find(original), Is.Empty);
            AssertSingleMatch(lookup.Find(replacement), recipe, StyleRecipeRelationship.Owner, replacement);
            AssertSingleMatch(lookup.Find(consumer), recipe, StyleRecipeRelationship.Consumer, consumer);
            recipe.ConsumerPrefabs[0] = null;
            Assert.That(lookup.Find(consumer), Is.Empty);
            Assert.That(EditorUtility.GetDirtyCount(recipe), Is.EqualTo(dirtyCount));
            AssertFilesUnchanged(before);
        }

        [Test]
        public void DuplicateCatalogEntriesProduceUniqueMatchesInStablePathOrder()
        {
            GameObject prefab = CreatePrefab("Owner.prefab");
            PrefabStyleRecipe first = CreateRecipe("A Recipe.asset", prefab);
            PrefabStyleRecipe second = CreateRecipe("Z Recipe.asset", prefab);
            StyleRecipeLookup lookup = CreateLookup(second, first, second, null, first);

            IReadOnlyList<StyleRecipeMatch> matches = lookup.Find(prefab);
            Assert.That(matches.Select(match => match.Recipe), Is.EqualTo(new[] { first, second }));
            Assert.That(lookup.Find(prefab).Select(match => match.Recipe), Is.EqualTo(new[] { first, second }));
            lookup.Invalidate();
            Assert.That(lookup.Find(prefab).Select(match => match.Recipe), Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void NullNonPrefabAndRecipeSelectionsHaveNoMatches()
        {
            GameObject prefab = CreatePrefab("Owner.prefab");
            PrefabStyleRecipe recipe = CreateRecipe("Recipe.asset", prefab);
            StyleRecipeLookup lookup = CreateLookup(recipe);
            var sceneObject = new GameObject("Not A Prefab");
            SceneManager.MoveGameObjectToScene(sceneObject, _testScene);

            Assert.That(lookup.Find(null), Is.Empty);
            Assert.That(lookup.Find(sceneObject), Is.Empty);
            Assert.That(lookup.Find(sceneObject.transform), Is.Empty);
            Assert.That(lookup.Find(recipe), Is.Empty);
        }

        private GameObject CreatePrefab(string fileName, GameObject nestedPrefab = null)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(fileName), typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, _testScene);
            try
            {
                var label = new GameObject("Label", typeof(RectTransform));
                label.transform.SetParent(root.transform, false);
                if (nestedPrefab != null)
                {
                    var nested = (GameObject)PrefabUtility.InstantiatePrefab(nestedPrefab, _testScene);
                    nested.transform.SetParent(root.transform, false);
                }

                return PrefabUtility.SaveAsPrefabAsset(root, _testRoot + "/" + fileName);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private GameObject CreateVariant(string fileName, GameObject basePrefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, _testScene);
            try
            {
                return PrefabUtility.SaveAsPrefabAsset(instance, _testRoot + "/" + fileName);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private PrefabStyleRecipe CreateRecipe(string fileName, GameObject owner, params GameObject[] consumers)
        {
            var recipe = ScriptableObject.CreateInstance<PrefabStyleRecipe>();
            SetPrivateField(recipe, "_ownerPrefab", owner);
            SetPrivateField(recipe, "_consumerPrefabs", consumers);
            AssetDatabase.CreateAsset(recipe, _testRoot + "/" + fileName);
            AssetDatabase.SaveAssetIfDirty(recipe);
            return recipe;
        }

        private static StyleRecipeLookup CreateLookup(params PrefabStyleRecipe[] recipes)
        {
            return new StyleRecipeLookup(() => recipes);
        }

        private static void AssertSingleMatch(IReadOnlyList<StyleRecipeMatch> matches,
            PrefabStyleRecipe recipe, StyleRecipeRelationship relationship, GameObject prefab)
        {
            Assert.That(matches.Count, Is.EqualTo(1));
            AssertMatch(matches[0], recipe, relationship, prefab);
        }

        private static void AssertMatch(StyleRecipeMatch match, PrefabStyleRecipe recipe,
            StyleRecipeRelationship relationship, GameObject prefab)
        {
            Assert.That(match.Recipe, Is.EqualTo(recipe));
            Assert.That(match.Relationship, Is.EqualTo(relationship));
            Assert.That(match.PrefabPath, Is.EqualTo(AssetDatabase.GetAssetPath(prefab)));
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing fixture field: " + name);
            field.SetValue(target, value);
        }

        private IReadOnlyDictionary<string, byte[]> CaptureFiles()
        {
            return Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        }

        private void AssertFilesUnchanged(IReadOnlyDictionary<string, byte[]> before)
        {
            IReadOnlyDictionary<string, byte[]> after = CaptureFiles();
            Assert.That(after.Keys, Is.EquivalentTo(before.Keys));
            foreach (KeyValuePair<string, byte[]> file in before)
            {
                Assert.That(after[file.Key], Is.EqualTo(file.Value), "Lookup wrote " + file.Key);
            }
        }
    }
}
