using System;
using System.Collections.Generic;

using TMPro;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    internal static class PrefabTargetResolver
    {
        internal readonly struct CapturedTarget
        {
            public string GlobalObjectId { get; }
            public string DisplayPath { get; }
            public string ComponentType { get; }

            public CapturedTarget(
                string globalObjectId,
                string displayPath,
                string componentType)
            {
                GlobalObjectId = globalObjectId;
                DisplayPath = displayPath;
                ComponentType = componentType;
            }
        }

        public static bool TryCaptureSelection(
            GameObject ownerPrefab,
            PrefabTargetKind expectedKind,
            out CapturedTarget capturedTarget,
            out string error)
        {
            capturedTarget = default;
            error = null;
            if (!TryGetPrefabPath(ownerPrefab, out string ownerPath, out error))
            {
                return false;
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null
                || !string.Equals(stage.assetPath, ownerPath, StringComparison.OrdinalIgnoreCase)
                || stage.prefabContentsRoot == null)
            {
                error = "Open the recipe owner in Prefab Mode before capturing a target.";
                return false;
            }

            if (stage.scene.isDirty)
            {
                error = "Save the owner Prefab before capturing a target.";
                return false;
            }

            Type expectedType = GetExpectedType(expectedKind);
            if (!TryGetSelectedComponent(
                stage.prefabContentsRoot,
                expectedType,
                out Component selectedComponent,
                out error))
            {
                return false;
            }

            if (!TryCreateLocator(
                stage.prefabContentsRoot,
                selectedComponent,
                out PrefabComponentLocator locator,
                out error))
            {
                return false;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(ownerPath);
                Component assetComponent = FindComponentByLocator(root, locator);
                if (assetComponent == null || !expectedType.IsInstanceOfType(assetComponent))
                {
                    error = "The selected Prefab Mode component does not match the saved owner Prefab.";
                    return false;
                }

                string globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(assetComponent).ToString();
                if (!GlobalObjectId.TryParse(globalObjectId, out _))
                {
                    error = "Unity could not create a stable GlobalObjectId for the selected component.";
                    return false;
                }

                capturedTarget = new CapturedTarget(
                    globalObjectId,
                    GetDisplayPath(root.transform, assetComponent.transform),
                    assetComponent.GetType().FullName);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Could not capture the Prefab target: {exception.Message}";
                return false;
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        public static bool OpenOwnerAndSelectTarget(
            PrefabStyleRecipe recipe,
            PrefabTargetReference targetReference,
            out string error)
        {
            error = null;
            if (recipe == null || targetReference == null)
            {
                error = "The recipe or target reference is missing.";
                return false;
            }

            if (!TryGetPrefabPath(recipe.OwnerPrefab, out string ownerPath, out error))
            {
                return false;
            }

            error = StyleRecipeProcessor.GetUnsavedTrackedStageError(recipe);
            if (!string.IsNullOrEmpty(error))
            {
                return false;
            }

            GameObject root = null;
            PrefabComponentLocator locator;
            try
            {
                root = PrefabUtility.LoadPrefabContents(ownerPath);
                if (!TryResolveTarget(
                    root,
                    targetReference,
                    GetExpectedType(targetReference.ExpectedKind),
                    out Component component,
                    out error))
                {
                    return false;
                }

                if (!TryCreateLocator(root, component, out locator, out error))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = $"Could not resolve the saved Prefab target: {exception.Message}";
                return false;
            }
            finally
            {
                if (root != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || !string.Equals(stage.assetPath, ownerPath, StringComparison.OrdinalIgnoreCase))
            {
                stage = PrefabStageUtility.OpenPrefab(ownerPath);
            }

            if (stage == null || stage.prefabContentsRoot == null)
            {
                error = $"Could not open Prefab Mode for {ownerPath}.";
                return false;
            }

            Component stageComponent = FindComponentByLocator(stage.prefabContentsRoot, locator);
            if (stageComponent == null)
            {
                error = $"Could not locate the target in Prefab Mode: {targetReference.DisplayPath}.";
                return false;
            }

            Selection.activeGameObject = stageComponent.gameObject;
            EditorGUIUtility.PingObject(stageComponent.gameObject);
            return true;
        }

        public static bool TryResolveTarget(
            GameObject root,
            PrefabTargetReference targetReference,
            Type expectedType,
            out Component component,
            out string error)
        {
            return TryResolveTarget(
                new TargetIndex(root),
                targetReference,
                expectedType,
                out component,
                out error);
        }

        internal static bool TryResolveTarget(
            TargetIndex targetIndex,
            PrefabTargetReference targetReference,
            Type expectedType,
            out Component component,
            out string error)
        {
            component = null;
            error = null;
            if (targetReference == null
                || string.IsNullOrWhiteSpace(targetReference.GlobalObjectId)
                || !GlobalObjectId.TryParse(targetReference.GlobalObjectId, out _))
            {
                error = "The target has not been captured or its GlobalObjectId is invalid.";
                return false;
            }

            Component resolvedComponent = targetIndex.FindComponent(targetReference.GlobalObjectId);
            if (resolvedComponent == null)
            {
                error = $"The target no longer exists: {targetReference.DisplayPath}.";
                return false;
            }

            if (!expectedType.IsInstanceOfType(resolvedComponent))
            {
                error =
                    $"The target type changed at {targetReference.DisplayPath}. "
                    + $"Expected {expectedType.Name}, found {resolvedComponent.GetType().Name}.";
                return false;
            }

            if (!string.IsNullOrEmpty(targetReference.ComponentType)
                && !string.Equals(
                    targetReference.ComponentType,
                    resolvedComponent.GetType().FullName,
                    StringComparison.Ordinal))
            {
                error =
                    $"The captured component changed at {targetReference.DisplayPath}. "
                    + $"Capture the target again.";
                return false;
            }

            component = resolvedComponent;
            return true;
        }

        public static Type GetExpectedType(PrefabTargetKind targetKind)
        {
            return targetKind switch
            {
                PrefabTargetKind.Graphic => typeof(Graphic),
                PrefabTargetKind.Image => typeof(Image),
                PrefabTargetKind.Text => typeof(TMP_Text),
                PrefabTargetKind.Selectable => typeof(Selectable),
                _ => throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, null),
            };
        }

        private static bool TryGetPrefabPath(
            GameObject prefab,
            out string path,
            out string error)
        {
            path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
            if (string.IsNullOrEmpty(path)
                || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "The recipe owner must reference a Prefab asset.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryGetSelectedComponent(
            GameObject root,
            Type expectedType,
            out Component component,
            out string error)
        {
            component = Selection.activeObject as Component;
            if (component != null && !expectedType.IsInstanceOfType(component))
            {
                component = null;
            }

            GameObject selectedGameObject = Selection.activeGameObject;
            if (component == null && selectedGameObject != null)
            {
                Component[] candidates = selectedGameObject.GetComponents(expectedType);
                if (candidates.Length == 1)
                {
                    component = candidates[0];
                }
                else if (candidates.Length > 1)
                {
                    error =
                        $"{selectedGameObject.name} has more than one {expectedType.Name}. "
                        + "Select the exact component in the Inspector.";
                    return false;
                }
            }

            if (component == null)
            {
                error = $"Select a GameObject with one {expectedType.Name} component.";
                return false;
            }

            Transform targetTransform = component.transform;
            if (targetTransform != root.transform && !targetTransform.IsChildOf(root.transform))
            {
                error = "The selected component is outside the recipe owner Prefab.";
                component = null;
                return false;
            }

            error = null;
            return true;
        }

        internal static bool TryCreateLocator(
            GameObject root,
            Component component,
            out PrefabComponentLocator locator,
            out string error)
        {
            locator = default;
            if (!TryGetSiblingIndexChain(
                root.transform,
                component.transform,
                out int[] siblingIndices))
            {
                error = "Could not calculate a path from the Prefab root to the target.";
                return false;
            }

            Component[] components = component.gameObject.GetComponents(component.GetType());
            int componentIndex = Array.IndexOf(components, component);
            if (componentIndex < 0)
            {
                error = "Could not determine the component occurrence on its GameObject.";
                return false;
            }

            locator = new PrefabComponentLocator(
                siblingIndices,
                component.GetType(),
                componentIndex);
            error = null;
            return true;
        }

        private static bool TryGetSiblingIndexChain(
            Transform root,
            Transform target,
            out int[] siblingIndices)
        {
            var indices = new List<int>();
            Transform current = target;
            while (current != null && current != root)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            if (current != root)
            {
                siblingIndices = null;
                return false;
            }

            indices.Reverse();
            siblingIndices = indices.ToArray();
            return true;
        }

        internal static Component FindComponentByLocator(
            GameObject root,
            PrefabComponentLocator locator)
        {
            Transform targetTransform = root.transform;
            foreach (int siblingIndex in locator.SiblingIndices)
            {
                if (siblingIndex < 0 || siblingIndex >= targetTransform.childCount)
                {
                    return null;
                }

                targetTransform = targetTransform.GetChild(siblingIndex);
            }

            Component[] components = targetTransform.GetComponents(locator.ComponentType);
            return locator.ComponentIndex >= 0 && locator.ComponentIndex < components.Length
                ? components[locator.ComponentIndex]
                : null;
        }

        internal static string GetDisplayPath(Transform root, Transform target)
        {
            string relativePath = AnimationUtility.CalculateTransformPath(target, root);
            string objectPath = string.IsNullOrEmpty(relativePath)
                ? root.name
                : $"{root.name}/{relativePath}";
            return objectPath;
        }

        internal readonly struct PrefabComponentLocator
        {
            public int[] SiblingIndices { get; }
            public Type ComponentType { get; }
            public int ComponentIndex { get; }

            public PrefabComponentLocator(
                int[] siblingIndices,
                Type componentType,
                int componentIndex)
            {
                SiblingIndices = siblingIndices;
                ComponentType = componentType;
                ComponentIndex = componentIndex;
            }
        }

        /// <summary>Indexes one loaded Prefab root for the lifetime of its inspection.</summary>
        internal sealed class TargetIndex
        {
            private readonly GameObject _root;
            private Dictionary<string, Component> _componentsById;

            internal int BuildCount { get; private set; }
            internal int IndexedComponentCount { get; private set; }

            internal TargetIndex(GameObject root)
            {
                _root = root;
            }

            internal Component FindComponent(string globalObjectId)
            {
                if (_componentsById == null)
                {
                    Build();
                }

                return _componentsById.TryGetValue(globalObjectId, out Component component)
                    ? component
                    : null;
            }

            private void Build()
            {
                var components = new List<Component>();
                foreach (Transform transform in _root.GetComponentsInChildren<Transform>(true))
                {
                    foreach (Component component in transform.GetComponents<Component>())
                    {
                        if (component != null)
                        {
                            components.Add(component);
                        }
                    }
                }

                Object[] objects = components.ToArray();
                var identifiers = new GlobalObjectId[objects.Length];
                GlobalObjectId.GetGlobalObjectIdsSlow(objects, identifiers);

                var componentsById = new Dictionary<string, Component>(
                    components.Count,
                    StringComparer.Ordinal);
                for (int index = 0; index < identifiers.Length; index++)
                {
                    string identifier = identifiers[index].ToString();
                    if (!componentsById.ContainsKey(identifier))
                    {
                        componentsById.Add(identifier, components[index]);
                    }
                }

                _componentsById = componentsById;
                IndexedComponentCount = components.Count;
                BuildCount++;
            }
        }
    }
}
