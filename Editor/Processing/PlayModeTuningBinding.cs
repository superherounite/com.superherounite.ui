using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    internal enum PlayModeTuningProperty
    {
        GraphicColor,
        PixelsPerUnitMultiplier,
        NormalColor,
        HighlightedColor,
        PressedColor,
        SelectedColor,
        DisabledColor,
    }

    internal sealed class PlayModeTuningBinding
    {
        private readonly string _componentType;

        public PrefabStyleRecipe Recipe { get; }
        public PrefabTargetReference TargetReference { get; }
        public string Label { get; }
        public string Key { get; }
        public PlayModeTuningProperty Property { get; }
        public Object SourceAsset { get; }
        public bool IsColor => Property != PlayModeTuningProperty.PixelsPerUnitMultiplier;
        public Color SourceColor => IsColor
            ? ((ColorToken)SourceAsset).Value
            : throw new InvalidOperationException("This binding does not have a color value.");
        public float SourceFloat => !IsColor
            ? ((ImageStyle)SourceAsset).PixelsPerUnitMultiplier
            : throw new InvalidOperationException("This binding does not have a float value.");

        private PlayModeTuningBinding(
            PrefabStyleRecipe recipe,
            PrefabTargetReference targetReference,
            string label,
            PlayModeTuningProperty property,
            Object sourceAsset)
        {
            Recipe = recipe;
            TargetReference = targetReference;
            _componentType = targetReference.ComponentType;
            Label = $"{targetReference.DisplayPath} / {label}";
            Property = property;
            SourceAsset = sourceAsset;
            string recipeId = EditorUtility.IsPersistent(recipe)
                ? GlobalObjectId.GetGlobalObjectIdSlow(recipe).ToString()
                : recipe.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
            Key = $"{recipeId}|{targetReference.GlobalObjectId}|{property}";
        }

        public static IReadOnlyList<PlayModeTuningBinding> GetBindings(PrefabStyleRecipe recipe)
        {
            var bindings = new List<PlayModeTuningBinding>();
            if (recipe == null)
            {
                return bindings;
            }

            foreach (PrefabStyleRecipe.GraphicColorBinding binding in
                recipe.GraphicColors ?? Array.Empty<PrefabStyleRecipe.GraphicColorBinding>())
            {
                AddColor(bindings, recipe, binding?.Target, PrefabTargetKind.Graphic,
                    binding?.Color, "Graphic Color", PlayModeTuningProperty.GraphicColor);
            }

            foreach (PrefabStyleRecipe.ImageBinding binding in
                recipe.Images ?? Array.Empty<PrefabStyleRecipe.ImageBinding>())
            {
                AddImage(bindings, recipe, binding?.Target, binding?.Style, "Image");
            }

            foreach (PrefabStyleRecipe.SurfaceBinding binding in
                recipe.Surfaces ?? Array.Empty<PrefabStyleRecipe.SurfaceBinding>())
            {
                AddImage(bindings, recipe, binding?.FillTarget, binding?.Style?.Fill, "Surface Fill");
                AddImage(bindings, recipe, binding?.OutlineTarget, binding?.Style?.Outline, "Surface Outline");
            }

            foreach (PrefabStyleRecipe.TextBinding binding in
                recipe.Texts ?? Array.Empty<PrefabStyleRecipe.TextBinding>())
            {
                AddColor(bindings, recipe, binding?.Target, PrefabTargetKind.Text,
                    binding?.Style?.Color, "Text Color", PlayModeTuningProperty.GraphicColor);
            }

            foreach (PrefabStyleRecipe.SelectableBinding binding in
                recipe.Selectables ?? Array.Empty<PrefabStyleRecipe.SelectableBinding>())
            {
                SelectableStyle style = binding?.Style;
                AddColor(bindings, recipe, binding?.Target, PrefabTargetKind.Selectable,
                    style?.Normal, "Normal Color", PlayModeTuningProperty.NormalColor);
                AddColor(bindings, recipe, binding?.Target, PrefabTargetKind.Selectable,
                    style?.Highlighted, "Highlighted Color", PlayModeTuningProperty.HighlightedColor);
                AddColor(bindings, recipe, binding?.Target, PrefabTargetKind.Selectable,
                    style?.Pressed, "Pressed Color", PlayModeTuningProperty.PressedColor);
                AddColor(bindings, recipe, binding?.Target, PrefabTargetKind.Selectable,
                    style?.Selected, "Selected Color", PlayModeTuningProperty.SelectedColor);
                AddColor(bindings, recipe, binding?.Target, PrefabTargetKind.Selectable,
                    style?.Disabled, "Disabled Color", PlayModeTuningProperty.DisabledColor);
            }

            return bindings;
        }

        public static bool TryFindBinding(
            PrefabStyleRecipe recipe,
            string key,
            out PlayModeTuningBinding binding)
        {
            binding = null;
            foreach (PlayModeTuningBinding candidate in GetBindings(recipe))
            {
                if (!string.Equals(candidate.Key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (binding != null)
                {
                    binding = null;
                    return false;
                }

                binding = candidate;
            }

            return binding != null;
        }

        public bool ValidateTarget(Component target, out string error)
        {
            error = null;
            if (!IsLiveSceneTarget(target))
            {
                error = "Select a live scene component outside Prefab Mode and preview scenes.";
                return false;
            }

            if (!PrefabTargetResolver.GetExpectedType(TargetReference.ExpectedKind).IsInstanceOfType(target)
                || !string.Equals(TargetReference.ComponentType, target.GetType().FullName, StringComparison.Ordinal))
            {
                error = $"Select the captured component type: {TargetReference.ComponentType}.";
                return false;
            }

            string ownerPath = Recipe != null ? AssetDatabase.GetAssetPath(Recipe.OwnerPrefab) : null;
            if (string.IsNullOrEmpty(ownerPath)
                || !ownerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "The recipe owner must reference a saved Prefab asset.";
                return false;
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(target.gameObject))
            {
                // Runtime Instantiate clones have no source link. The user selects their component explicitly.
                return true;
            }

            Component source = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(target, ownerPath);
            GameObject sourceInstance = source != null
                ? PrefabUtility.GetNearestPrefabInstanceRoot(source.gameObject)
                : null;
            if (sourceInstance != null && sourceInstance != Recipe.OwnerPrefab)
            {
                error = "The captured target belongs to a nested Prefab. Select that Prefab's own Recipe.";
                return false;
            }

            GlobalObjectId sourceId = source != null ? GlobalObjectId.GetGlobalObjectIdSlow(source) : default;
            // Capture uses a loaded Prefab scene; source correspondence returns the asset representation.
            // Normalize the loaded scene's prefab instance encoding to the imported asset's local file ID.
            if (source == null || !GlobalObjectId.TryParse(TargetReference.GlobalObjectId, out GlobalObjectId capturedId)
                || sourceId.assetGUID != capturedId.assetGUID
                || GetPrefabAssetLocalId(sourceId) != GetPrefabAssetLocalId(capturedId))
            {
                error = "The selected Prefab instance does not match the captured target in this recipe owner.";
                return false;
            }

            return true;
        }

        private static ulong GetPrefabAssetLocalId(GlobalObjectId identifier)
        {
            // Unity's Prefab team documents this variant/nested-instance file ID encoding:
            // https://discussions.unity.com/t/nested-prefabs-fileid-reference-does-not-exist/765306
            return identifier.identifierType == 2 && identifier.targetPrefabId != 0
                ? (identifier.targetObjectId ^ identifier.targetPrefabId) & 0x7fffffffffffffffUL
                : identifier.targetObjectId;
        }

        public Color ReadColor(Component target)
        {
            RequirePropertyType(true);
            if (Property == PlayModeTuningProperty.GraphicColor)
            {
                return ((Graphic)target).color;
            }

            ColorBlock colors = ((Selectable)target).colors;
            return Property switch
            {
                PlayModeTuningProperty.NormalColor => colors.normalColor,
                PlayModeTuningProperty.HighlightedColor => colors.highlightedColor,
                PlayModeTuningProperty.PressedColor => colors.pressedColor,
                PlayModeTuningProperty.SelectedColor => colors.selectedColor,
                PlayModeTuningProperty.DisabledColor => colors.disabledColor,
                _ => throw new InvalidOperationException("Unsupported color property."),
            };
        }

        public float ReadFloat(Component target)
        {
            RequirePropertyType(false);
            return ((Image)target).pixelsPerUnitMultiplier;
        }

        public void WriteColor(Component target, Color value)
        {
            RequirePropertyType(true);
            RequireValidTarget(target);
            WriteColorCore(target, value);
        }

        public void WriteFloat(Component target, float value)
        {
            RequirePropertyType(false);
            RequireValidTarget(target);
            WriteFloatCore(target, value);
        }

        internal void RestoreColor(Component target, Color value)
        {
            RequirePropertyType(true);
            RequireRestorableTarget(target);
            WriteColorCore(target, value);
        }

        internal void RestoreFloat(Component target, float value)
        {
            RequirePropertyType(false);
            RequireRestorableTarget(target);
            WriteFloatCore(target, value);
        }

        private void WriteColorCore(Component target, Color value)
        {
            if (!IsFinite(value.r) || !IsFinite(value.g) || !IsFinite(value.b) || !IsFinite(value.a))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Color channels must be finite.");
            }

            if (Property == PlayModeTuningProperty.GraphicColor)
            {
                ((Graphic)target).color = value;
                return;
            }

            var selectable = (Selectable)target;
            ColorBlock colors = selectable.colors;
            switch (Property)
            {
                case PlayModeTuningProperty.NormalColor:
                {
                    colors.normalColor = value;
                    break;
                }
                case PlayModeTuningProperty.HighlightedColor:
                {
                    colors.highlightedColor = value;
                    break;
                }
                case PlayModeTuningProperty.PressedColor:
                {
                    colors.pressedColor = value;
                    break;
                }
                case PlayModeTuningProperty.SelectedColor:
                {
                    colors.selectedColor = value;
                    break;
                }
                case PlayModeTuningProperty.DisabledColor:
                {
                    colors.disabledColor = value;
                    break;
                }
                default:
                {
                    throw new InvalidOperationException("Unsupported color property.");
                }
            }

            selectable.colors = colors;
        }

        private static void WriteFloatCore(Component target, float value)
        {
            if (!IsFinite(value) || value < 0.01f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "PPUM must be finite and at least 0.01.");
            }

            ((Image)target).pixelsPerUnitMultiplier = value;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsLiveSceneTarget(Component target)
        {
            return target != null && !EditorUtility.IsPersistent(target)
                && target.gameObject.scene.IsValid() && target.gameObject.scene.isLoaded
                && !EditorSceneManager.IsPreviewScene(target.gameObject.scene)
                && PrefabStageUtility.GetPrefabStage(target.gameObject) == null;
        }

        private void RequireRestorableTarget(Component target)
        {
            Type propertyType = Property == PlayModeTuningProperty.GraphicColor
                ? typeof(Graphic)
                : Property == PlayModeTuningProperty.PixelsPerUnitMultiplier ? typeof(Image) : typeof(Selectable);
            if (!IsLiveSceneTarget(target) || !propertyType.IsInstanceOfType(target)
                || !string.Equals(_componentType, target.GetType().FullName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Restore requires the original concrete component type in a live scene.");
            }
        }

        private void RequirePropertyType(bool isColor)
        {
            if (IsColor != isColor)
            {
                throw new InvalidOperationException("The tuning value type does not match this binding.");
            }
        }

        private void RequireValidTarget(Component target)
        {
            if (!ValidateTarget(target, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        private static void AddImage(
            ICollection<PlayModeTuningBinding> bindings,
            PrefabStyleRecipe recipe,
            PrefabTargetReference target,
            ImageStyle style,
            string label)
        {
            AddColor(bindings, recipe, target, PrefabTargetKind.Image, style?.Tint,
                label + " Color", PlayModeTuningProperty.GraphicColor);
            if (style != null && style.OwnsPixelsPerUnitMultiplier && HasTarget(target, PrefabTargetKind.Image))
            {
                bindings.Add(new PlayModeTuningBinding(recipe, target, label + " PPUM",
                    PlayModeTuningProperty.PixelsPerUnitMultiplier, style));
            }
        }

        private static void AddColor(
            ICollection<PlayModeTuningBinding> bindings,
            PrefabStyleRecipe recipe,
            PrefabTargetReference target,
            PrefabTargetKind expectedKind,
            ColorToken token,
            string label,
            PlayModeTuningProperty property)
        {
            if (token != null && HasTarget(target, expectedKind))
            {
                bindings.Add(new PlayModeTuningBinding(recipe, target, label, property, token));
            }
        }

        private static bool HasTarget(PrefabTargetReference target, PrefabTargetKind expectedKind)
        {
            return target != null && target.ExpectedKind == expectedKind
                && !string.IsNullOrEmpty(target.ComponentType)
                && GlobalObjectId.TryParse(target.GlobalObjectId, out GlobalObjectId identifier)
                && identifier.targetObjectId != 0;
        }
    }
}
