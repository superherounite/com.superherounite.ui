using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using UnityEditor;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    public static partial class StyleRecipeProcessor
    {
        /// <summary>Previews reverting owned overrides in explicitly registered Prefabs without writing.</summary>
        public static StyleReview PreviewOverrideRepairs(StyleRecipeRegistry registry)
        {
            StyleReview review = PreviewFull(registry).CreateOverrideRepairReview();
            foreach (string path in review.OverrideRepairs.Select(repair => repair.AssetPath).Distinct())
            {
                if (EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(path)))
                {
                    review.AddError($"{path} has unsaved changes. Save it before repairing overrides.");
                }
            }

            var fingerprintText = new StringBuilder(Fingerprint(registry, review));
            foreach (StyleOverrideRepair repair in review.OverrideRepairs)
            {
                fingerprintText.AppendLine();
                fingerprintText.Append(repair.Key);
            }

            using SHA256 hash = SHA256.Create();
            review.Fingerprint = Convert.ToBase64String(
                hash.ComputeHash(Encoding.UTF8.GetBytes(fingerprintText.ToString())));
            return review;
        }

        /// <summary>Reverts only reviewed owned overrides, then returns a fresh ordinary Preview.</summary>
        public static StyleReview ApplyOverrideRepairs(StyleRecipeRegistry registry, StyleReview approvedReview)
        {
            RequireWritableEditorState();
            if (approvedReview == null || approvedReview.Registry != registry || !approvedReview.IsOverrideRepairReview)
            {
                throw new InvalidOperationException("Preview override repairs and review them before applying.");
            }

            StyleReview current = PreviewOverrideRepairs(registry);
            if (!string.Equals(current.Fingerprint, approvedReview.Fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The registry or one of its dependencies changed after Preview. Preview override repairs again.");
            }

            if (current.State == StyleReviewState.Error)
            {
                throw new InvalidOperationException(string.Join("\n", current.Errors));
            }

            var dependencies = new StyleAssetDependencySnapshot();
            IGrouping<string, StyleOverrideRepair>[] groups = current.OverrideRepairs
                .GroupBy(repair => repair.AssetPath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => dependencies.GetDependencies(group.Key).Length)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            try
            {
                foreach (IGrouping<string, StyleOverrideRepair> group in groups)
                {
                    ApplyOverrideRepairs(group.Key, group);
                }
            }
            finally
            {
                ClearPreviewCache();
            }

            return Preview(registry);
        }

        private static void ApplyOverrideRepairs(string assetPath, IEnumerable<StyleOverrideRepair> repairs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            try
            {
                bool changed = false;
                foreach (StyleOverrideRepair repair in repairs)
                {
                    Component component = PrefabTargetResolver.FindComponentByLocator(root, repair.Locator);
                    if (component == null)
                    {
                        throw new InvalidOperationException($"{assetPath}: the reviewed override target no longer resolves.");
                    }

                    string currentGlobalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(component).ToString();
                    if (!string.Equals(
                        StyleOverrideRepair.NormalizeGlobalObjectId(currentGlobalObjectId),
                        StyleOverrideRepair.NormalizeGlobalObjectId(repair.GlobalObjectId),
                        StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{assetPath}: the reviewed override target identity changed "
                            + $"from {repair.GlobalObjectId} to {currentGlobalObjectId}. Preview override repairs again.");
                    }

                    using var serializedObject = new SerializedObject(component);
                    serializedObject.UpdateIfRequiredOrScript();
                    SerializedProperty property = serializedObject.FindProperty(repair.PropertyPath);
                    if (property == null)
                    {
                        throw new InvalidOperationException($"{assetPath}: missing reviewed property {repair.PropertyPath}.");
                    }

                    if (HasPrefabOverride(property))
                    {
                        PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                        changed = true;
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, assetPath, out bool success);
                    if (!success)
                    {
                        throw new InvalidOperationException($"Could not save override repairs to {assetPath}.");
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
