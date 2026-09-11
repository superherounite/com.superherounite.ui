using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using TMPro;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Previews, validates, and bakes an explicit registry of prefab style recipes.</summary>
    public static partial class StyleRecipeProcessor
    {
        public static StyleReview Preview(StyleRecipeRegistry registry)
        {
            RequireReadableEditorState();
            return InspectPreview(registry, true);
        }

        public static StyleReview Apply(
            StyleRecipeRegistry registry,
            StyleReview approvedReview)
        {
            RequireWritableEditorState();
            if (approvedReview == null || approvedReview.Registry != registry
                || approvedReview.IsOverrideRepairReview)
            {
                throw new InvalidOperationException(
                    "Preview this registry and review its changes before applying them.");
            }

            StyleReview current = Preview(registry);
            if (!string.Equals(
                current.Fingerprint,
                approvedReview.Fingerprint,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The registry or one of its dependencies changed after Preview. Preview it again.");
            }

            if (current.State == StyleReviewState.Error)
            {
                throw new InvalidOperationException(string.Join("\n", current.Errors));
            }

            if (current.State == StyleReviewState.Ready)
            {
                return current;
            }

            // Reuse dependency queries only while planning, before the first Prefab save.
            var prefabDependencies = new StyleAssetDependencySnapshot();
            HashSet<string> affectedOwners = GetAffectedOwnerPaths(
                registry, current.ChangedOwnerPaths, prefabDependencies);
            InvalidatePreviewOwners(registry, affectedOwners, prefabDependencies);
            StyleReview applied = Inspect(registry, affectedOwners, prefabDependencies);
            if (applied.State == StyleReviewState.Error)
            {
                throw new InvalidOperationException(string.Join("\n", applied.Errors));
            }

            StyleReview result = Preview(registry);
            result.AppliedOwnerPrefabCount = applied.OwnerPrefabLoadCount;
            if (result.State != StyleReviewState.Ready)
            {
                throw new InvalidOperationException(
                    $"The registry is not Ready after Apply.\n{result}");
            }

            return result;
        }

        internal static string GetUnsavedTrackedStageError(PrefabStyleRecipe recipe)
        {
            if (recipe == null)
            {
                return null;
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || !stage.scene.isDirty)
            {
                return null;
            }

            string stagePath = stage.assetPath;
            if (string.Equals(
                stagePath,
                AssetDatabase.GetAssetPath(recipe.OwnerPrefab),
                StringComparison.OrdinalIgnoreCase))
            {
                return $"{stagePath} has unsaved changes in Prefab Mode. Save or close it first.";
            }

            foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
            {
                if (string.Equals(
                    stagePath,
                    AssetDatabase.GetAssetPath(consumer),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return $"{stagePath} has unsaved changes in Prefab Mode. Save or close it first.";
                }
            }

            return null;
        }

        internal static bool HasUnsavedDependencies(StyleRecipeRegistry registry)
        {
            if (registry == null || EditorUtility.IsDirty(registry))
            {
                return true;
            }

            foreach (PrefabStyleRecipe recipe in registry.Recipes ?? Array.Empty<PrefabStyleRecipe>())
            {
                if (recipe == null
                    || EditorUtility.IsDirty(recipe)
                    || EditorUtility.IsDirty(recipe.OwnerPrefab)
                    || !string.IsNullOrEmpty(GetUnsavedTrackedStageError(recipe)))
                {
                    return true;
                }

                foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
                {
                    if (consumer == null || EditorUtility.IsDirty(consumer))
                    {
                        return true;
                    }
                }

                foreach (Object dependency in GetRecipeAuthoringDependencies(recipe))
                {
                    if (dependency == null || EditorUtility.IsDirty(dependency))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static string GetDependencyFingerprint(StyleRecipeRegistry registry)
        {
            return StyleRegistryDependencyFingerprint.Capture(registry);
        }

        private static StyleReview Inspect(
            StyleRecipeRegistry registry,
            ISet<string> affectedOwners,
            StyleAssetDependencySnapshot prefabDependencies)
        {
            var review = new StyleReview
            {
                Registry = registry,
            };
            List<PrefabStyleRecipe> recipes = GetRecipes(registry, review);
            List<PrefabStyleRecipe> affectedRecipes = SortByPrefabDependency(
                recipes.Where(recipe => affectedOwners.Contains(AssetDatabase.GetAssetPath(recipe.OwnerPrefab)))
                    .ToList(),
                prefabDependencies);

            foreach (PrefabStyleRecipe recipe in affectedRecipes)
            {
                string ownerPath = AssetDatabase.GetAssetPath(recipe.OwnerPrefab);
                if (InspectOwner(recipe, review, true, out List<ManagedTarget> managedTargets))
                {
                    InspectConsumers(recipe, recipes, ownerPath, managedTargets, review, null);
                }
            }

            return review;
        }

        private static List<PrefabStyleRecipe> GetRecipes(
            StyleRecipeRegistry registry,
            StyleReview review)
        {
            var recipes = new List<PrefabStyleRecipe>();
            if (registry == null)
            {
                review.AddError("A Style Recipe Registry is required.");
                return recipes;
            }

            string registryPath = AssetDatabase.GetAssetPath(registry);
            if (string.IsNullOrEmpty(registryPath))
            {
                review.AddError("The Style Recipe Registry must be a saved project asset.");
            }
            else
            {
                review.AddAsset(registryPath);
            }

            var recipePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ownerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PrefabStyleRecipe[] registeredRecipes = registry.Recipes;
            if (registeredRecipes == null || registeredRecipes.Length == 0)
            {
                review.AddError($"{registry.name} does not contain any recipes.");
                return recipes;
            }

            for (int index = 0; index < registeredRecipes.Length; index++)
            {
                PrefabStyleRecipe recipe = registeredRecipes[index];
                if (recipe == null)
                {
                    review.AddError($"{registry.name} Recipe[{index}] is missing.");
                    continue;
                }

                string recipePath = AssetDatabase.GetAssetPath(recipe);
                if (string.IsNullOrEmpty(recipePath))
                {
                    review.AddError($"{recipe.name} must be a saved project asset.");
                    continue;
                }

                if (!recipePaths.Add(recipePath))
                {
                    review.AddError($"Duplicate recipe: {recipePath}");
                    continue;
                }

                string ownerPath = AssetDatabase.GetAssetPath(recipe.OwnerPrefab);
                if (!string.IsNullOrEmpty(ownerPath) && !ownerPaths.Add(ownerPath))
                {
                    review.AddError(
                        $"Only one recipe may own a Prefab in a registry: {ownerPath}");
                    continue;
                }

                recipes.Add(recipe);
                review.AddAsset(recipePath);
            }

            foreach (PrefabStyleRecipe recipe in recipes)
            {
                if (recipe.BaseRecipe == null)
                {
                    continue;
                }

                if (!recipes.Contains(recipe.BaseRecipe))
                {
                    review.AddError($"{recipe.name} Base Recipe is not registered in {registry.name}.");
                }
                else if (!IsPrefabVariantOf(recipe.OwnerPrefab, recipe.BaseRecipe.OwnerPrefab))
                {
                    review.AddError(
                        $"{recipe.name} owner must be a Prefab Variant of its Base Recipe owner.");
                }
            }

            return recipes;
        }

        private static bool InspectOwner(
            PrefabStyleRecipe recipe,
            StyleReview review,
            bool apply,
            out List<ManagedTarget> managedTargets)
        {
            managedTargets = new List<ManagedTarget>();
            if (recipe.OwnerPrefab == null)
            {
                review.AddError($"{recipe.name} has no owner Prefab.");
                return false;
            }

            string ownerPath = AssetDatabase.GetAssetPath(recipe.OwnerPrefab);
            if (string.IsNullOrEmpty(ownerPath)
                || !ownerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                review.AddError($"{recipe.name} owner is not a Prefab asset.");
                return false;
            }

            review.AddAsset(ownerPath);
            string stageError = GetUnsavedTrackedStageError(recipe);
            if (!string.IsNullOrEmpty(stageError))
            {
                review.AddError(stageError);
                return false;
            }

            int bindingCount = (recipe.GraphicColors?.Length ?? 0)
                + (recipe.Images?.Length ?? 0)
                + (recipe.Surfaces?.Length ?? 0)
                + (recipe.Texts?.Length ?? 0)
                + (recipe.Selectables?.Length ?? 0);
            if (bindingCount == 0)
            {
                review.AddError($"{recipe.name} does not contain any style bindings.");
                return false;
            }

            GameObject root = null;
            try
            {
                review.OwnerPrefabLoadCount++;
                if (!apply)
                {
                    review.CanReuseInspection &= StylePrefabInspectionPolicy.CanReuse(recipe.OwnerPrefab);
                }

                root = PrefabUtility.LoadPrefabContents(ownerPath);
                if (!apply)
                {
                    review.CanReuseInspection &= StylePrefabInspectionPolicy.CanReuse(root);
                }

                var context = new RecipeContext(
                    ownerPath,
                    root,
                    review,
                    managedTargets,
                    apply);
                context.CheckMissingScripts();
                context.Apply(recipe);
                if (apply && context.HasChanges && review.Errors.Count == 0)
                {
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ownerPath);
                    if (saved == null)
                    {
                        review.AddError($"Could not save the owner Prefab: {ownerPath}");
                    }
                }
            }
            catch (Exception exception)
            {
                review.AddError($"{ownerPath}: recipe processing failed: {exception.Message}");
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return true;
        }

        private static void InspectConsumers(
            PrefabStyleRecipe recipe,
            IReadOnlyList<PrefabStyleRecipe> recipes,
            string ownerPath,
            IReadOnlyList<ManagedTarget> managedTargets,
            StyleReview review,
            ConsumerInspectionBatch consumers,
            List<StyleReview> consumerReviews = null)
        {
            var consumerPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
            {
                StyleReview consumerReview = consumers != null ? consumers.AddReview() : review;
                consumerReviews?.Add(consumerReview);
                string consumerPath = AssetDatabase.GetAssetPath(consumer);
                if (consumer == null
                    || string.IsNullOrEmpty(consumerPath)
                    || !consumerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    consumerReview.AddError($"{recipe.name} contains an invalid consumer Prefab.");
                    continue;
                }

                if (string.Equals(consumerPath, ownerPath, StringComparison.OrdinalIgnoreCase))
                {
                    consumerReview.AddError($"The owner cannot also be its own consumer: {ownerPath}");
                    continue;
                }

                if (!consumerPaths.Add(consumerPath))
                {
                    consumerReview.AddError($"Duplicate consumer Prefab: {consumerPath}");
                    continue;
                }

                consumerReview.AddAsset(consumerPath);
                if (consumers != null)
                {
                    consumers.Add(consumerPath, recipe, ownerPath, managedTargets, consumerReview);
                }
                else
                {
                    InspectConsumer(consumerPath, recipe, recipes, ownerPath, managedTargets, consumerReview);
                }
            }
        }

        private static void InspectConsumer(
            string consumerPath,
            PrefabStyleRecipe ownerRecipe,
            IReadOnlyList<PrefabStyleRecipe> recipes,
            string ownerPath,
            IReadOnlyList<ManagedTarget> managedTargets,
            StyleReview review)
        {
            GameObject root = null;
            try
            {
                review.ConsumerPrefabLoadCount++;
                root = PrefabUtility.LoadPrefabContents(consumerPath);
                var context = new ConsumerContext(root, consumerPath, recipes);
                InspectConsumer(context, ownerRecipe, ownerPath, managedTargets, review);
            }
            catch (Exception exception)
            {
                review.AddError($"{consumerPath}: consumer validation failed: {exception.Message}");
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static void InspectConsumer(
            ConsumerContext context,
            PrefabStyleRecipe ownerRecipe,
            string ownerPath,
            IReadOnlyList<ManagedTarget> managedTargets,
            StyleReview review,
            bool resolveCurrentOwners = true)
        {
            string consumerPath = context.AssetPath;
            try
            {
                review.CanReuseInspection &= context.CanReuseInspection;
                foreach (string error in context.MissingScriptErrors)
                {
                    review.AddError(error);
                }

                IReadOnlyList<GameObject> ownerInstances = context.GetOwnerInstances(ownerPath);
                if (ownerInstances.Count == 0)
                {
                    if (resolveCurrentOwners && InspectCurrentRegisteredOwners(context, review))
                    {
                        return;
                    }

                    review.AddError($"{consumerPath} does not contain an instance of {ownerPath} "
                        + "or any current registered Recipe owner. Register the replacement owner's Recipe.");
                    return;
                }
                foreach (GameObject ownerInstance in ownerInstances)
                {
                    string instancePath = PrefabTargetResolver.GetDisplayPath(
                        context.Root.transform,
                        ownerInstance.transform);
                    IReadOnlyDictionary<string, Component> componentsBySourceId =
                        FindCorrespondingComponents(ownerInstance, managedTargets);
                    foreach (ManagedTarget managedTarget in managedTargets)
                    {
                        if (!componentsBySourceId.TryGetValue(
                            managedTarget.GlobalObjectId,
                            out Component component))
                        {
                            review.AddError(
                                $"{consumerPath}/{instancePath}: cannot resolve "
                                + $"{managedTarget.DisplayPath}.");
                            continue;
                        }

                        CheckConsumerOverrides(
                            consumerPath,
                            instancePath,
                            ownerRecipe,
                            context.RecipeLookup,
                            ownerPath,
                            managedTarget,
                            component,
                            review);
                    }
                }
            }
            catch (Exception exception)
            {
                review.AddError($"{consumerPath}: consumer validation failed: {exception.Message}");
            }
        }

        private static bool InspectCurrentRegisteredOwners(ConsumerContext context, StyleReview review)
        {
            // A listed consumer stays tracked when its nested Prefab is replaced.
            // Re-resolve this uncommon path each time so newly present owners and live bindings cannot be missed.
            review.CanReuseInspection = false;
            bool foundOwner = false;
            foreach (PrefabStyleRecipe recipe in context.Recipes)
            {
                string ownerPath = AssetDatabase.GetAssetPath(recipe.OwnerPrefab);
                bool isConsumerOwner = string.Equals(ownerPath, context.AssetPath, StringComparison.OrdinalIgnoreCase);
                if (!isConsumerOwner && context.GetOwnerInstances(ownerPath).Count == 0)
                {
                    continue;
                }

                foundOwner = true;
                OwnerInspection owner = context.GetOwnerInspection(recipe);
                foreach (string error in owner.Review.Errors)
                {
                    review.AddError(error);
                }

                if (!isConsumerOwner && owner.InspectConsumers)
                {
                    InspectConsumer(context, recipe, ownerPath, owner.ManagedTargets, review, false);
                }
            }

            return foundOwner;
        }

        private static IReadOnlyDictionary<string, Component> FindCorrespondingComponents(
            GameObject instanceRoot,
            IReadOnlyList<ManagedTarget> managedTargets)
        {
            var targetIdsByAssetObjectId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ManagedTarget managedTarget in managedTargets)
            {
                foreach (string sourceKey in managedTarget.SourceKeys)
                {
                    targetIdsByAssetObjectId.TryAdd(
                        sourceKey,
                        managedTarget.GlobalObjectId);
                }
            }

            var result = new Dictionary<string, Component>(StringComparer.Ordinal);
            foreach (Transform transform in instanceRoot.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component component in transform.GetComponents<Component>())
                {
                    Object current = component;
                    while (current != null)
                    {
                        GlobalObjectId currentId = GlobalObjectId.GetGlobalObjectIdSlow(current);
                        if (targetIdsByAssetObjectId.TryGetValue(
                            GetAssetObjectKey(currentId),
                            out string matchingTargetId))
                        {
                            result.TryAdd(matchingTargetId, component);
                            break;
                        }

                        if (!TryGetNextPrefabSource(current, out Object source))
                        {
                            break;
                        }

                        current = source;
                    }
                }
            }

            return result;
        }

        private static string GetAssetObjectKey(GlobalObjectId globalObjectId)
        {
            return $"{globalObjectId.assetGUID}:{globalObjectId.targetObjectId}";
        }

        private static IReadOnlyCollection<string> GetPrefabSourceKeys(Component component)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<int>();
            Object current = component;
            while (current != null && visited.Add(current.GetInstanceID()))
            {
                GlobalObjectId currentId = GlobalObjectId.GetGlobalObjectIdSlow(current);
                keys.Add(GetAssetObjectKey(currentId));
                if (!TryGetNextPrefabSource(current, out Object source))
                {
                    break;
                }

                current = source;
            }

            return keys;
        }

        private static bool TryGetNextPrefabSource(Object current, out Object source)
        {
            source = current != null
                ? PrefabUtility.GetCorrespondingObjectFromSource(current)
                : null;
            return source != null && source != current;
        }

        private static void CheckConsumerOverrides(
            string consumerPath,
            string instancePath,
            PrefabStyleRecipe ownerRecipe,
            StyleConsumerRecipeLookup recipes,
            string ownerPath,
            ManagedTarget managedTarget,
            Component component,
            StyleReview review)
        {
            var visited = new HashSet<int>();
            Object current = component;
            bool isConsumerComponent = true;
            while (current is Component currentComponent
                && visited.Add(currentComponent.GetInstanceID()))
            {
                string currentPath = isConsumerComponent
                    ? consumerPath
                    : AssetDatabase.GetAssetPath(currentComponent);
                if (string.Equals(
                    currentPath,
                    ownerPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var serializedObject = new SerializedObject(currentComponent);
                serializedObject.UpdateIfRequiredOrScript();
                foreach (string propertyPath in managedTarget.PropertyPaths)
                {
                    SerializedProperty property = serializedObject.FindProperty(propertyPath);
                    if (property == null)
                    {
                        review.AddError(
                            $"{consumerPath}/{instancePath}: missing serialized property "
                            + $"{managedTarget.DisplayPath}.{propertyPath}.");
                        continue;
                    }

                    if (HasPrefabOverride(property)
                        && !IsExplicitSpecialization(
                            ownerRecipe,
                            recipes,
                            currentPath,
                            managedTarget,
                            propertyPath))
                    {
                        string sourceLabel = string.IsNullOrEmpty(currentPath)
                            || string.Equals(
                                currentPath,
                                consumerPath,
                                StringComparison.OrdinalIgnoreCase)
                            ? consumerPath
                            : currentPath;
                        string error =
                            $"{consumerPath}/{instancePath}: {managedTarget.DisplayPath}."
                            + $"{propertyPath} overrides a recipe-owned value in {sourceLabel}.";
                        if (recipes.GetOwners(currentPath).Any(recipe =>
                            RecipeOwnsProperty(recipes, recipe, managedTarget, propertyPath)))
                        {
                            review.AddError(error + " This property is owned by another registered Recipe. "
                                + "Declare its Base Recipe specialization before applying changes.");
                        }
                        else if (recipes.IsRegisteredPrefab(currentPath)
                            && PrefabTargetResolver.TryCreateLocator(
                                currentComponent.transform.root.gameObject,
                                currentComponent,
                                out PrefabTargetResolver.PrefabComponentLocator locator,
                                out _))
                        {
                            string displayPath = PrefabTargetResolver.GetDisplayPath(
                                currentComponent.transform.root, currentComponent.transform);
                            review.AddOverrideError(error, new StyleOverrideRepair(
                                currentPath, locator, GlobalObjectId.GetGlobalObjectIdSlow(currentComponent).ToString(),
                                propertyPath, displayPath, error));
                        }
                        else
                        {
                            review.AddError(error + " Register the source Prefab as a consumer "
                                + "to review its override repairs, or define an explicit Base Recipe specialization.");
                        }
                    }
                }

                if (!TryGetNextPrefabSource(currentComponent, out Object source))
                {
                    break;
                }

                current = source;
                isConsumerComponent = false;
            }
        }

        private static bool HasPrefabOverride(SerializedProperty property)
        {
            if (property.prefabOverride)
            {
                return true;
            }

            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren)
                && !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.prefabOverride)
                {
                    return true;
                }

                enterChildren = false;
            }

            return false;
        }

        private static bool IsExplicitSpecialization(
            PrefabStyleRecipe baseRecipe,
            StyleConsumerRecipeLookup recipes,
            string specializationOwnerPath,
            ManagedTarget baseTarget,
            string propertyPath)
        {
            foreach (PrefabStyleRecipe recipe in recipes.GetOwners(specializationOwnerPath))
            {
                if (!recipes.IsSpecializationOf(recipe, baseRecipe))
                {
                    continue;
                }

                if (RecipeOwnsProperty(recipes, recipe, baseTarget, propertyPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RecipeOwnsProperty(
            StyleConsumerRecipeLookup recipes,
            PrefabStyleRecipe recipe,
            ManagedTarget baseTarget,
            string propertyPath)
        {
            foreach (PrefabStyleRecipe.GraphicColorBinding binding
                in recipe.GraphicColors ?? Array.Empty<PrefabStyleRecipe.GraphicColorBinding>())
            {
                if (propertyPath == "m_Color" && TargetsMatch(recipes, binding?.Target, baseTarget))
                {
                    return true;
                }
            }

            foreach (PrefabStyleRecipe.ImageBinding binding
                in recipe.Images ?? Array.Empty<PrefabStyleRecipe.ImageBinding>())
            {
                if (ImageStyleOwnsProperty(binding?.Style, propertyPath)
                    && TargetsMatch(recipes, binding?.Target, baseTarget))
                {
                    return true;
                }
            }

            foreach (PrefabStyleRecipe.SurfaceBinding binding
                in recipe.Surfaces ?? Array.Empty<PrefabStyleRecipe.SurfaceBinding>())
            {
                if (ImageStyleOwnsProperty(binding?.Style?.Fill, propertyPath)
                    && TargetsMatch(recipes, binding?.FillTarget, baseTarget))
                {
                    return true;
                }

                if (ImageStyleOwnsProperty(binding?.Style?.Outline, propertyPath)
                    && TargetsMatch(recipes, binding?.OutlineTarget, baseTarget))
                {
                    return true;
                }
            }

            foreach (PrefabStyleRecipe.TextBinding binding
                in recipe.Texts ?? Array.Empty<PrefabStyleRecipe.TextBinding>())
            {
                if (TextStyleOwnsProperty(propertyPath)
                    && TargetsMatch(recipes, binding?.Target, baseTarget))
                {
                    return true;
                }
            }

            foreach (PrefabStyleRecipe.SelectableBinding binding
                in recipe.Selectables ?? Array.Empty<PrefabStyleRecipe.SelectableBinding>())
            {
                if (SelectableStyleOwnsProperty(propertyPath)
                    && TargetsMatch(recipes, binding?.Target, baseTarget))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TargetsMatch(
            StyleConsumerRecipeLookup recipes,
            PrefabTargetReference target,
            ManagedTarget baseTarget)
        {
            return recipes.TargetsMatch(target, baseTarget.SourceKeys, baseTarget.DisplayPath, baseTarget.ComponentType);
        }

        private static bool ImageStyleOwnsProperty(ImageStyle style, string propertyPath)
        {
            if (style == null)
            {
                return false;
            }

            return propertyPath == "m_Color"
                || (style.OwnsSprite && propertyPath == "m_Sprite")
                || (style.OwnsType && propertyPath == "m_Type")
                || (style.OwnsPreserveAspect && propertyPath == "m_PreserveAspect")
                || (style.OwnsFillCenter && propertyPath == "m_FillCenter")
                || (style.OwnsPixelsPerUnitMultiplier && propertyPath == "m_PixelsPerUnitMultiplier");
        }

        private static bool TextStyleOwnsProperty(string propertyPath)
        {
            return propertyPath == "m_fontAsset"
                || propertyPath == "m_sharedMaterial"
                || propertyPath == "m_fontColor"
                || propertyPath == "m_fontSize"
                || propertyPath == "m_fontSizeBase"
                || propertyPath == "m_fontStyle"
                || propertyPath == "m_enableAutoSizing"
                || propertyPath == "m_fontSizeMin"
                || propertyPath == "m_fontSizeMax"
                || propertyPath == "m_characterSpacing"
                || propertyPath == "m_wordSpacing"
                || propertyPath == "m_lineSpacing"
                || propertyPath == "m_paragraphSpacing";
        }

        private static bool SelectableStyleOwnsProperty(string propertyPath)
        {
            return propertyPath == "m_Transition"
                || propertyPath == "m_Colors.m_NormalColor"
                || propertyPath == "m_Colors.m_HighlightedColor"
                || propertyPath == "m_Colors.m_PressedColor"
                || propertyPath == "m_Colors.m_SelectedColor"
                || propertyPath == "m_Colors.m_DisabledColor"
                || propertyPath == "m_Colors.m_ColorMultiplier"
                || propertyPath == "m_Colors.m_FadeDuration";
        }

        private static bool IsPrefabVariantOf(GameObject candidate, GameObject basePrefab)
        {
            Object current = candidate;
            while (TryGetNextPrefabSource(current, out Object source))
            {
                if (source == basePrefab)
                {
                    return true;
                }

                current = source;
            }

            return false;
        }

        private static void CheckMissingScripts(
            GameObject root,
            string assetPath,
            StyleReview review)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                {
                    string path = PrefabTargetResolver.GetDisplayPath(root.transform, transform);
                    review.AddError($"{assetPath}/{path}: Missing Script.");
                }
            }
        }

        private static List<PrefabStyleRecipe> SortByPrefabDependency(
            List<PrefabStyleRecipe> recipes,
            StyleAssetDependencySnapshot prefabDependencies)
        {
            var ownerPaths = new HashSet<string>(
                recipes.Select(recipe => AssetDatabase.GetAssetPath(recipe.OwnerPrefab)),
                StringComparer.OrdinalIgnoreCase);
            return recipes
                .OrderBy(recipe => prefabDependencies.GetDependencies(
                        AssetDatabase.GetAssetPath(recipe.OwnerPrefab))
                    .Count(ownerPaths.Contains))
                .ThenBy(recipe => AssetDatabase.GetAssetPath(recipe.OwnerPrefab), StringComparer.Ordinal)
                .ToList();
        }

        private static string Fingerprint(
            StyleRecipeRegistry registry,
            StyleReview review)
        {
            var text = new StringBuilder();
            review.DependencyFingerprint = GetDependencyFingerprint(registry);
            text.AppendLine(review.DependencyFingerprint);
            text.AppendLine(string.Join("\n", review.Errors));
            text.AppendLine(string.Join("\n", review.Changes));
            using SHA256 hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        internal static IEnumerable<Object> GetRecipeAuthoringDependencies(
            PrefabStyleRecipe recipe)
        {
            var dependencies = new HashSet<Object>();
            foreach (PrefabStyleRecipe.GraphicColorBinding binding
                in recipe.GraphicColors ?? Array.Empty<PrefabStyleRecipe.GraphicColorBinding>())
            {
                AddDependency(dependencies, binding?.Color);
            }

            foreach (PrefabStyleRecipe.ImageBinding binding
                in recipe.Images ?? Array.Empty<PrefabStyleRecipe.ImageBinding>())
            {
                AddImageStyleDependencies(dependencies, binding?.Style);
            }

            foreach (PrefabStyleRecipe.SurfaceBinding binding
                in recipe.Surfaces ?? Array.Empty<PrefabStyleRecipe.SurfaceBinding>())
            {
                AddDependency(dependencies, binding?.Style);
                AddImageStyleDependencies(dependencies, binding?.Style?.Fill);
                AddImageStyleDependencies(dependencies, binding?.Style?.Outline);
            }

            foreach (PrefabStyleRecipe.TextBinding binding
                in recipe.Texts ?? Array.Empty<PrefabStyleRecipe.TextBinding>())
            {
                AddDependency(dependencies, binding?.Style);
                AddDependency(dependencies, binding?.Style?.Font);
                AddDependency(dependencies, binding?.Style?.Color);
            }

            foreach (PrefabStyleRecipe.SelectableBinding binding
                in recipe.Selectables ?? Array.Empty<PrefabStyleRecipe.SelectableBinding>())
            {
                SelectableStyle style = binding?.Style;
                AddDependency(dependencies, style);
                AddDependency(dependencies, style?.Normal);
                AddDependency(dependencies, style?.Highlighted);
                AddDependency(dependencies, style?.Pressed);
                AddDependency(dependencies, style?.Selected);
                AddDependency(dependencies, style?.Disabled);
            }

            return dependencies;
        }

        private static void AddImageStyleDependencies(
            ISet<Object> dependencies,
            ImageStyle style)
        {
            AddDependency(dependencies, style);
            AddDependency(dependencies, style?.Tint);
            AddDependency(dependencies, style?.Sprite);
        }

        private static void AddDependency(ISet<Object> dependencies, Object dependency)
        {
            if (dependency != null)
            {
                dependencies.Add(dependency);
            }
        }

        private static void RequireWritableEditorState()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException(
                    "Wait for Edit Mode, compilation, and asset import to finish.");
            }
        }

        private static void RequireReadableEditorState()
        {
            if (EditorApplication.isPlaying
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException(
                    "Wait for Edit Mode, compilation, and asset import to finish.");
            }
        }

        private sealed class ConsumerInspectionBatch
        {
            private readonly List<StyleReview> _reviews = new();
            private readonly Dictionary<PrefabStyleRecipe, OwnerInspection> _owners = new();
            private readonly Dictionary<string, List<ConsumerInspection>> _requests =
                new(StringComparer.OrdinalIgnoreCase);

            public StyleReview AddReview(StyleReview review = null)
            {
                review ??= new StyleReview();
                _reviews.Add(review);
                return review;
            }

            public void AddOwner(PrefabStyleRecipe recipe, OwnerInspection owner)
            {
                _owners.Add(recipe, owner);
            }

            public void Add(
                string consumerPath,
                PrefabStyleRecipe recipe,
                string ownerPath,
                IReadOnlyList<ManagedTarget> managedTargets,
                StyleReview review)
            {
                if (!_requests.TryGetValue(consumerPath, out List<ConsumerInspection> requests))
                {
                    requests = new List<ConsumerInspection>();
                    _requests.Add(consumerPath, requests);
                }

                requests.Add(new ConsumerInspection(recipe, ownerPath, managedTargets, review));
            }

            public void Inspect(IReadOnlyList<PrefabStyleRecipe> recipes, StyleReview result)
            {
                // Only read-only inspections are batched. Apply validates fresh consumers
                // immediately after each owner save, in Prefab dependency order.
                foreach (KeyValuePair<string, List<ConsumerInspection>> entry in _requests)
                {
                    GameObject root = null;
                    try
                    {
                        result.ConsumerPrefabLoadCount++;
                        root = PrefabUtility.LoadPrefabContents(entry.Key);
                        var context = new ConsumerContext(root, entry.Key, recipes,
                            recipe => _owners.TryGetValue(recipe, out OwnerInspection owner) ? owner : null);
                        foreach (ConsumerInspection request in entry.Value)
                        {
                            InspectConsumer(
                                context,
                                request.Recipe,
                                request.OwnerPath,
                                request.ManagedTargets,
                                request.Review);
                        }
                    }
                    catch (Exception exception)
                    {
                        foreach (ConsumerInspection request in entry.Value)
                        {
                            request.Review.AddError(
                                $"{entry.Key}: consumer validation failed: {exception.Message}");
                        }
                    }
                    finally
                    {
                        if (root != null)
                        {
                            PrefabUtility.UnloadPrefabContents(root);
                        }
                    }
                }

                // Preserve the original recipe/consumer order of every diagnostic and asset.
                foreach (StyleReview review in _reviews)
                {
                    result.Append(review);
                }
            }
        }

        private sealed class ConsumerInspection
        {
            public PrefabStyleRecipe Recipe { get; }
            public string OwnerPath { get; }
            public IReadOnlyList<ManagedTarget> ManagedTargets { get; }
            public StyleReview Review { get; }

            public ConsumerInspection(
                PrefabStyleRecipe recipe,
                string ownerPath,
                IReadOnlyList<ManagedTarget> managedTargets,
                StyleReview review)
            {
                Recipe = recipe;
                OwnerPath = ownerPath;
                ManagedTargets = managedTargets;
                Review = review;
            }
        }

        private sealed class ConsumerContext
        {
            private readonly Dictionary<PrefabStyleRecipe, OwnerInspection> _owners = new();
            private readonly Func<PrefabStyleRecipe, OwnerInspection> _getOwnerInspection;
            private readonly Dictionary<string, List<GameObject>> _instancesByOwner =
                new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, IReadOnlyList<GameObject>> _outermostInstances =
                new(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _missingScriptErrors = new();

            public GameObject Root { get; }
            public string AssetPath { get; }
            public IReadOnlyList<PrefabStyleRecipe> Recipes { get; }
            public IReadOnlyList<string> MissingScriptErrors => _missingScriptErrors;
            public bool CanReuseInspection { get; }
            public StyleConsumerRecipeLookup RecipeLookup { get; }

            public ConsumerContext(
                GameObject root,
                string assetPath,
                IReadOnlyList<PrefabStyleRecipe> recipes,
                Func<PrefabStyleRecipe, OwnerInspection> getOwnerInspection = null)
            {
                Root = root;
                AssetPath = assetPath;
                Recipes = recipes;
                _getOwnerInspection = getOwnerInspection;
                RecipeLookup = new StyleConsumerRecipeLookup(recipes, targetId =>
                    GlobalObjectId.GlobalObjectIdentifierToObjectSlow(targetId) is Component component
                        ? GetPrefabSourceKeys(component)
                        : Array.Empty<string>());
                CanReuseInspection = StylePrefabInspectionPolicy.CanReuse(
                    AssetDatabase.LoadAssetAtPath<GameObject>(assetPath))
                    && StylePrefabInspectionPolicy.CanReuse(root);
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject candidate = transform.gameObject;
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(candidate) > 0)
                    {
                        string path = PrefabTargetResolver.GetDisplayPath(root.transform, transform);
                        _missingScriptErrors.Add($"{assetPath}/{path}: Missing Script.");
                    }

                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(candidate))
                    {
                        continue;
                    }

                    var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var visited = new HashSet<int>();
                    Object current = candidate;
                    while (visited.Add(current.GetInstanceID())
                        && TryGetNextPrefabSource(current, out Object source))
                    {
                        string ownerPath = AssetDatabase.GetAssetPath(source);
                        if (sourcePaths.Add(ownerPath))
                        {
                            if (!_instancesByOwner.TryGetValue(ownerPath, out List<GameObject> instances))
                            {
                                instances = new List<GameObject>();
                                _instancesByOwner.Add(ownerPath, instances);
                            }

                            instances.Add(candidate);
                        }

                        current = source;
                    }
                }
            }

            public OwnerInspection GetOwnerInspection(PrefabStyleRecipe recipe)
            {
                if (!_owners.TryGetValue(recipe, out OwnerInspection owner))
                {
                    owner = _getOwnerInspection?.Invoke(recipe);
                    if (owner == null)
                    {
                        var review = new StyleReview();
                        bool inspectConsumers = InspectOwner(recipe, review, false, out List<ManagedTarget> targets);
                        owner = new OwnerInspection(review, targets, inspectConsumers);
                    }

                    _owners.Add(recipe, owner);
                }

                return owner;
            }

            public IReadOnlyList<GameObject> GetOwnerInstances(string ownerPath)
            {
                if (_outermostInstances.TryGetValue(ownerPath, out IReadOnlyList<GameObject> cached))
                {
                    return cached;
                }

                if (!_instancesByOwner.TryGetValue(ownerPath, out List<GameObject> candidates))
                {
                    return Array.Empty<GameObject>();
                }

                var matchingTransforms = new HashSet<Transform>(candidates.Select(candidate => candidate.transform));
                var result = new List<GameObject>();
                foreach (GameObject candidate in candidates)
                {
                    Transform ancestor = candidate.transform.parent;
                    while (ancestor != null && !matchingTransforms.Contains(ancestor))
                    {
                        ancestor = ancestor.parent;
                    }

                    if (ancestor == null)
                    {
                        result.Add(candidate);
                    }
                }

                _outermostInstances.Add(ownerPath, result);
                return result;
            }
        }

        private sealed class ManagedTarget
        {
            private readonly HashSet<string> _propertyPaths = new(StringComparer.Ordinal);

            public string GlobalObjectId { get; }
            public string DisplayPath { get; }
            public string ComponentType { get; }
            public IReadOnlyCollection<string> SourceKeys { get; }
            public IReadOnlyCollection<string> PropertyPaths => _propertyPaths;

            public ManagedTarget(
                string globalObjectId,
                string displayPath,
                string componentType,
                IReadOnlyCollection<string> sourceKeys)
            {
                GlobalObjectId = globalObjectId;
                DisplayPath = displayPath;
                ComponentType = componentType;
                SourceKeys = sourceKeys;
            }

            public void AddProperty(string propertyPath)
            {
                _propertyPaths.Add(propertyPath);
            }
        }

        private sealed class RecipeContext
        {
            private readonly string _ownerPath;
            private readonly GameObject _root;
            private readonly PrefabTargetResolver.TargetIndex _targetIndex;
            private readonly StyleReview _review;
            private readonly List<ManagedTarget> _managedTargets;
            private readonly bool _apply;
            private readonly Dictionary<string, ManagedTarget> _managedByTargetId =
                new(StringComparer.Ordinal);
            private readonly HashSet<string> _propertyOwners = new(StringComparer.Ordinal);

            public bool HasChanges { get; private set; }

            public RecipeContext(
                string ownerPath,
                GameObject root,
                StyleReview review,
                List<ManagedTarget> managedTargets,
                bool apply)
            {
                _ownerPath = ownerPath;
                _root = root;
                _targetIndex = new PrefabTargetResolver.TargetIndex(root);
                _review = review;
                _managedTargets = managedTargets;
                _apply = apply;
            }

            public void CheckMissingScripts()
            {
                StyleRecipeProcessor.CheckMissingScripts(_root, _ownerPath, _review);
            }

            public void Apply(PrefabStyleRecipe recipe)
            {
                ApplyGraphicColors(recipe.GraphicColors);
                ApplyImages(recipe.Images);
                ApplySurfaces(recipe.Surfaces);
                ApplyTexts(recipe.Texts);
                ApplySelectables(recipe.Selectables);
            }

            private void ApplyGraphicColors(
                PrefabStyleRecipe.GraphicColorBinding[] bindings)
            {
                for (int index = 0; index < (bindings?.Length ?? 0); index++)
                {
                    PrefabStyleRecipe.GraphicColorBinding binding = bindings[index];
                    if (binding == null)
                    {
                        Error($"Graphic Color[{index}] is missing.");
                        continue;
                    }

                    Graphic target = Resolve<Graphic>(
                        binding.Target,
                        PrefabTargetKind.Graphic,
                        $"Graphic Color[{index}]");
                    ColorToken color = Require(binding.Color, $"Graphic Color[{index}] Color");
                    if (target != null && color != null)
                    {
                        Type colorOwner = target.GetType()
                            .GetProperty(nameof(Graphic.color))?
                            .DeclaringType;
                        if (colorOwner != typeof(Graphic))
                        {
                            Error(
                                $"Graphic Color[{index}] target {target.GetType().Name} "
                                + "uses a specialized color property. Use its typed binding instead.");
                            continue;
                        }

                        ChangeColor(
                            binding.Target,
                            target,
                            "m_Color",
                            "color",
                            color.Value);
                    }
                }
            }

            private void ApplyImages(PrefabStyleRecipe.ImageBinding[] bindings)
            {
                for (int index = 0; index < (bindings?.Length ?? 0); index++)
                {
                    PrefabStyleRecipe.ImageBinding binding = bindings[index];
                    if (binding == null)
                    {
                        Error($"Image[{index}] is missing.");
                        continue;
                    }

                    Image image = Resolve<Image>(
                        binding.Target,
                        PrefabTargetKind.Image,
                        $"Image[{index}]");
                    ImageStyle style = Require(binding.Style, $"Image[{index}] Style");
                    if (image != null && style != null)
                    {
                        ApplyImage(binding.Target, image, style, $"Image[{index}]");
                    }
                }
            }

            private void ApplySurfaces(PrefabStyleRecipe.SurfaceBinding[] bindings)
            {
                for (int index = 0; index < (bindings?.Length ?? 0); index++)
                {
                    PrefabStyleRecipe.SurfaceBinding binding = bindings[index];
                    if (binding == null)
                    {
                        Error($"Surface[{index}] is missing.");
                        continue;
                    }

                    SurfaceStyle style = Require(binding.Style, $"Surface[{index}] Style");
                    if (style == null)
                    {
                        continue;
                    }

                    ImageStyle fillStyle = Require(style.Fill, $"Surface[{index}] Fill Style");
                    Image fill = Resolve<Image>(
                        binding.FillTarget,
                        PrefabTargetKind.Image,
                        $"Surface[{index}] Fill Target");
                    if (fill != null && fillStyle != null)
                    {
                        ApplyImage(binding.FillTarget, fill, fillStyle, $"Surface[{index}] Fill");
                    }

                    bool hasOutlineTarget = IsAssigned(binding.OutlineTarget);
                    if (style.Outline == null && hasOutlineTarget)
                    {
                        Error($"Surface[{index}] has an Outline target but its Style has no Outline.");
                    }
                    else if (style.Outline != null && !hasOutlineTarget)
                    {
                        Error($"Surface[{index}] Style defines an Outline but no target is captured.");
                    }
                    else if (style.Outline != null)
                    {
                        Image outline = Resolve<Image>(
                            binding.OutlineTarget,
                            PrefabTargetKind.Image,
                            $"Surface[{index}] Outline Target");
                        if (outline != null)
                        {
                            ApplyImage(
                                binding.OutlineTarget,
                                outline,
                                style.Outline,
                                $"Surface[{index}] Outline");
                        }
                    }
                }
            }

            private void ApplyTexts(PrefabStyleRecipe.TextBinding[] bindings)
            {
                for (int index = 0; index < (bindings?.Length ?? 0); index++)
                {
                    PrefabStyleRecipe.TextBinding binding = bindings[index];
                    if (binding == null)
                    {
                        Error($"Text[{index}] is missing.");
                        continue;
                    }

                    TMP_Text text = Resolve<TMP_Text>(
                        binding.Target,
                        PrefabTargetKind.Text,
                        $"Text[{index}]");
                    TextStyle style = Require(binding.Style, $"Text[{index}] Style");
                    if (text != null && style != null)
                    {
                        ApplyText(binding.Target, text, style, $"Text[{index}]");
                    }
                }
            }

            private void ApplySelectables(PrefabStyleRecipe.SelectableBinding[] bindings)
            {
                for (int index = 0; index < (bindings?.Length ?? 0); index++)
                {
                    PrefabStyleRecipe.SelectableBinding binding = bindings[index];
                    if (binding == null)
                    {
                        Error($"Selectable[{index}] is missing.");
                        continue;
                    }

                    Selectable selectable = Resolve<Selectable>(
                        binding.Target,
                        PrefabTargetKind.Selectable,
                        $"Selectable[{index}]");
                    SelectableStyle style = Require(
                        binding.Style,
                        $"Selectable[{index}] Style");
                    if (selectable != null && style != null)
                    {
                        ApplySelectable(
                            binding.Target,
                            selectable,
                            style,
                            $"Selectable[{index}]");
                    }
                }
            }

            private void ApplyImage(
                PrefabTargetReference targetReference,
                Image image,
                ImageStyle style,
                string label)
            {
                ColorToken tint = Require(style.Tint, $"{label} Tint");
                if (tint == null)
                {
                    return;
                }

                ChangeColor(
                    targetReference,
                    image,
                    "m_Color",
                    "color",
                    tint.Value);

                if (style.OwnsSprite)
                {
                    ChangeObjectReference(
                        targetReference,
                        image,
                        "m_Sprite",
                        "sprite",
                        style.Sprite);
                }

                if (style.OwnsType)
                {
                    ChangeInteger(
                        targetReference,
                        image,
                        "m_Type",
                        "type",
                        (int)style.Type,
                        style.Type.ToString());
                }

                if (style.OwnsPreserveAspect)
                {
                    ChangeBoolean(
                        targetReference,
                        image,
                        "m_PreserveAspect",
                        "preserveAspect",
                        style.PreserveAspect);
                }

                if (style.OwnsFillCenter)
                {
                    ChangeBoolean(
                        targetReference,
                        image,
                        "m_FillCenter",
                        "fillCenter",
                        style.FillCenter);
                }

                if (style.OwnsPixelsPerUnitMultiplier)
                {
                    if (!IsFinite(style.PixelsPerUnitMultiplier)
                        || style.PixelsPerUnitMultiplier < 0.01f)
                    {
                        Error(
                            $"{label} Pixels Per Unit Multiplier must be finite and at least 0.01.");
                    }
                    else
                    {
                        ChangeFloat(
                            targetReference,
                            image,
                            "m_PixelsPerUnitMultiplier",
                            "pixelsPerUnitMultiplier",
                            style.PixelsPerUnitMultiplier);
                    }
                }
            }

            private void ApplyText(
                PrefabTargetReference targetReference,
                TMP_Text text,
                TextStyle style,
                string label)
            {
                TMP_FontAsset font = Require(style.Font, $"{label} Font");
                ColorToken color = Require(style.Color, $"{label} Color");
                Material sharedMaterial = font != null
                    ? Require(font.material, $"{label} Font Material")
                    : null;
                bool validNumbers = ValidateFiniteNonNegative(style.FontSize, $"{label} Font Size")
                    & ValidateFiniteNonNegative(style.FontSizeMin, $"{label} Font Size Min")
                    & ValidateFiniteNonNegative(style.FontSizeMax, $"{label} Font Size Max")
                    & ValidateFinite(style.CharacterSpacing, $"{label} Character Spacing")
                    & ValidateFinite(style.WordSpacing, $"{label} Word Spacing")
                    & ValidateFinite(style.LineSpacing, $"{label} Line Spacing")
                    & ValidateFinite(style.ParagraphSpacing, $"{label} Paragraph Spacing");
                if (style.FontSizeMin > style.FontSizeMax)
                {
                    Error($"{label} Font Size Min cannot exceed Font Size Max.");
                    validNumbers = false;
                }

                if (font == null || color == null || sharedMaterial == null || !validNumbers)
                {
                    return;
                }

                ChangeObjectReference(
                    targetReference,
                    text,
                    "m_fontAsset",
                    "font",
                    font);
                ChangeObjectReference(
                    targetReference,
                    text,
                    "m_sharedMaterial",
                    "fontSharedMaterial",
                    sharedMaterial);
                ChangeColor(
                    targetReference,
                    text,
                    "m_fontColor",
                    "color",
                    color.Value);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_fontSize",
                    "fontSize",
                    style.FontSize);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_fontSizeBase",
                    "fontSizeBase",
                    style.FontSize);
                ChangeInteger(
                    targetReference,
                    text,
                    "m_fontStyle",
                    "fontStyle",
                    (int)style.FontStyle,
                    style.FontStyle.ToString());
                ChangeBoolean(
                    targetReference,
                    text,
                    "m_enableAutoSizing",
                    "enableAutoSizing",
                    style.EnableAutoSizing);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_fontSizeMin",
                    "fontSizeMin",
                    style.FontSizeMin);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_fontSizeMax",
                    "fontSizeMax",
                    style.FontSizeMax);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_characterSpacing",
                    "characterSpacing",
                    style.CharacterSpacing);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_wordSpacing",
                    "wordSpacing",
                    style.WordSpacing);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_lineSpacing",
                    "lineSpacing",
                    style.LineSpacing);
                ChangeFloat(
                    targetReference,
                    text,
                    "m_paragraphSpacing",
                    "paragraphSpacing",
                    style.ParagraphSpacing);
            }

            private void ApplySelectable(
                PrefabTargetReference targetReference,
                Selectable selectable,
                SelectableStyle style,
                string label)
            {
                ColorToken normal = Require(style.Normal, $"{label} Normal");
                ColorToken highlighted = Require(style.Highlighted, $"{label} Highlighted");
                ColorToken pressed = Require(style.Pressed, $"{label} Pressed");
                ColorToken selected = Require(style.Selected, $"{label} Selected");
                ColorToken disabled = Require(style.Disabled, $"{label} Disabled");
                if (selectable.targetGraphic == null)
                {
                    Error($"{label} targetGraphic is missing.");
                }

                bool validNumbers = ValidateFiniteNonNegative(
                        style.ColorMultiplier,
                        $"{label} Color Multiplier")
                    & ValidateFiniteNonNegative(style.FadeDuration, $"{label} Fade Duration");

                if (normal == null
                    || highlighted == null
                    || pressed == null
                    || selected == null
                    || disabled == null
                    || !validNumbers)
                {
                    return;
                }

                ChangeInteger(
                    targetReference,
                    selectable,
                    "m_Transition",
                    "transition",
                    (int)Selectable.Transition.ColorTint,
                    Selectable.Transition.ColorTint.ToString());
                ChangeColor(
                    targetReference,
                    selectable,
                    "m_Colors.m_NormalColor",
                    "colors.normalColor",
                    normal.Value);
                ChangeColor(
                    targetReference,
                    selectable,
                    "m_Colors.m_HighlightedColor",
                    "colors.highlightedColor",
                    highlighted.Value);
                ChangeColor(
                    targetReference,
                    selectable,
                    "m_Colors.m_PressedColor",
                    "colors.pressedColor",
                    pressed.Value);
                ChangeColor(
                    targetReference,
                    selectable,
                    "m_Colors.m_SelectedColor",
                    "colors.selectedColor",
                    selected.Value);
                ChangeColor(
                    targetReference,
                    selectable,
                    "m_Colors.m_DisabledColor",
                    "colors.disabledColor",
                    disabled.Value);
                ChangeFloat(
                    targetReference,
                    selectable,
                    "m_Colors.m_ColorMultiplier",
                    "colors.colorMultiplier",
                    style.ColorMultiplier);
                ChangeFloat(
                    targetReference,
                    selectable,
                    "m_Colors.m_FadeDuration",
                    "colors.fadeDuration",
                    style.FadeDuration);
            }

            private T Resolve<T>(
                PrefabTargetReference targetReference,
                PrefabTargetKind expectedKind,
                string label)
                where T : Component
            {
                if (targetReference == null || targetReference.ExpectedKind != expectedKind)
                {
                    Error($"{label} target metadata is invalid. Reopen or reimport the recipe asset.");
                    return null;
                }

                if (!PrefabTargetResolver.TryResolveTarget(
                    _targetIndex,
                    targetReference,
                    typeof(T),
                    out Component component,
                    out string error))
                {
                    Error($"{label}: {error}");
                    return null;
                }

                GameObject nearestInstance = PrefabUtility.GetNearestPrefabInstanceRoot(
                    component.gameObject);
                if (nearestInstance != null && nearestInstance != _root)
                {
                    Error(
                        $"{label} belongs to nested Prefab {nearestInstance.name}. "
                        + "Move this binding to that Prefab's recipe.");
                    return null;
                }

                return (T)component;
            }

            private T Require<T>(T value, string label)
                where T : Object
            {
                if (value == null)
                {
                    Error($"{label} is missing.");
                }

                return value;
            }

            private void Own(
                PrefabTargetReference targetReference,
                Component component,
                string propertyPath)
            {
                string propertyKey = $"{targetReference.GlobalObjectId}:{propertyPath}";
                if (!_propertyOwners.Add(propertyKey))
                {
                    Error(
                        $"Duplicate style ownership: {targetReference.DisplayPath}."
                        + propertyPath);
                    return;
                }

                if (!_managedByTargetId.TryGetValue(
                    targetReference.GlobalObjectId,
                    out ManagedTarget managedTarget))
                {
                    managedTarget = new ManagedTarget(
                        targetReference.GlobalObjectId,
                        PrefabTargetResolver.GetDisplayPath(
                            _root.transform,
                            component.transform),
                        component.GetType().FullName,
                        GetPrefabSourceKeys(component));
                    _managedByTargetId.Add(targetReference.GlobalObjectId, managedTarget);
                    _managedTargets.Add(managedTarget);
                }

                managedTarget.AddProperty(propertyPath);
            }

            private void ChangeBoolean(
                PrefabTargetReference targetReference,
                Component target,
                string propertyPath,
                string displayName,
                bool proposedValue)
            {
                if (!TryGetSerializedProperty(
                    target,
                    propertyPath,
                    out SerializedObject serializedObject,
                    out SerializedProperty property))
                {
                    return;
                }

                Own(targetReference, target, propertyPath);
                Change(
                    property.boolValue == proposedValue,
                    target,
                    displayName,
                    proposedValue.ToString(),
                    () =>
                    {
                        property.boolValue = proposedValue;
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    });
            }

            private void ChangeColor(
                PrefabTargetReference targetReference,
                Component target,
                string propertyPath,
                string displayName,
                Color proposedValue)
            {
                if (!IsFinite(proposedValue))
                {
                    Error($"{displayName} must contain finite color channels.");
                    return;
                }

                if (!TryGetSerializedProperty(
                    target,
                    propertyPath,
                    out SerializedObject serializedObject,
                    out SerializedProperty property))
                {
                    return;
                }

                Own(targetReference, target, propertyPath);
                Change(
                    property.colorValue.Equals(proposedValue),
                    target,
                    displayName,
                    FormatColor(proposedValue),
                    () =>
                    {
                        property.colorValue = proposedValue;
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    });
            }

            private void ChangeFloat(
                PrefabTargetReference targetReference,
                Component target,
                string propertyPath,
                string displayName,
                float proposedValue)
            {
                if (!TryGetSerializedProperty(
                    target,
                    propertyPath,
                    out SerializedObject serializedObject,
                    out SerializedProperty property))
                {
                    return;
                }

                Own(targetReference, target, propertyPath);
                Change(
                    property.floatValue.Equals(proposedValue),
                    target,
                    displayName,
                    FormatFloat(proposedValue),
                    () =>
                    {
                        property.floatValue = proposedValue;
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    });
            }

            private void ChangeInteger(
                PrefabTargetReference targetReference,
                Component target,
                string propertyPath,
                string displayName,
                int proposedValue,
                string proposedDisplayValue)
            {
                if (!TryGetSerializedProperty(
                    target,
                    propertyPath,
                    out SerializedObject serializedObject,
                    out SerializedProperty property))
                {
                    return;
                }

                Own(targetReference, target, propertyPath);
                Change(
                    property.intValue == proposedValue,
                    target,
                    displayName,
                    proposedDisplayValue,
                    () =>
                    {
                        property.intValue = proposedValue;
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    });
            }

            private void ChangeObjectReference(
                PrefabTargetReference targetReference,
                Component target,
                string propertyPath,
                string displayName,
                Object proposedValue)
            {
                if (!TryGetSerializedProperty(
                    target,
                    propertyPath,
                    out SerializedObject serializedObject,
                    out SerializedProperty property))
                {
                    return;
                }

                Own(targetReference, target, propertyPath);
                Change(
                    property.objectReferenceValue == proposedValue,
                    target,
                    displayName,
                    FormatObject(proposedValue),
                    () =>
                    {
                        property.objectReferenceValue = proposedValue;
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    });
            }

            private bool TryGetSerializedProperty(
                Component target,
                string propertyPath,
                out SerializedObject serializedObject,
                out SerializedProperty property)
            {
                serializedObject = new SerializedObject(target);
                serializedObject.UpdateIfRequiredOrScript();
                property = serializedObject.FindProperty(propertyPath);
                if (property != null)
                {
                    return true;
                }

                Error(
                    $"{target.GetType().Name} at "
                    + $"{PrefabTargetResolver.GetDisplayPath(_root.transform, target.transform)} "
                    + $"does not expose required serialized property {propertyPath}.");
                return false;
            }

            private bool ValidateFinite(float value, string label)
            {
                if (IsFinite(value))
                {
                    return true;
                }

                Error($"{label} must be finite.");
                return false;
            }

            private bool ValidateFiniteNonNegative(float value, string label)
            {
                if (IsFinite(value) && value >= 0f)
                {
                    return true;
                }

                Error($"{label} must be finite and zero or greater.");
                return false;
            }

            private void Change(
                bool matches,
                Component target,
                string property,
                string proposedValue,
                Action write)
            {
                if (matches)
                {
                    return;
                }

                string path = PrefabTargetResolver.GetDisplayPath(
                    _root.transform,
                    target.transform);
                _review.AddChange(
                    $"{_ownerPath}/{path} ({target.GetType().Name}).{property} -> {proposedValue}");
                _review.AddChangedOwner(_ownerPath);
                if (_apply)
                {
                    write();
                    HasChanges = true;
                }
            }

            private void Error(string message)
            {
                _review.AddError($"{_ownerPath}: {message}");
            }

            private static bool IsAssigned(PrefabTargetReference targetReference)
            {
                return targetReference != null
                    && !string.IsNullOrWhiteSpace(targetReference.GlobalObjectId);
            }

            private static string FormatColor(Color color)
            {
                return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
            }

            private static string FormatFloat(float value)
            {
                return value.ToString("R", CultureInfo.InvariantCulture);
            }

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }

            private static bool IsFinite(Color color)
            {
                return IsFinite(color.r)
                    && IsFinite(color.g)
                    && IsFinite(color.b)
                    && IsFinite(color.a);
            }

            private static string FormatObject(Object target)
            {
                return target == null
                    ? "None"
                    : AssetDatabase.GetAssetPath(target);
            }
        }
    }
}
