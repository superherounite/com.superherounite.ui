using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using UnityEditor;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    public static partial class StyleRecipeProcessor
    {
        private static ConditionalWeakTable<StyleRecipeRegistry, RegistryPreviewCache> s_previewCaches = new();

        internal static StyleReview PreviewFull(StyleRecipeRegistry registry)
        {
            RequireReadableEditorState();
            return InspectPreview(registry, false);
        }

        internal static void ClearPreviewCache()
        {
            s_previewCaches = new ConditionalWeakTable<StyleRecipeRegistry, RegistryPreviewCache>();
        }

        private static StyleReview InspectPreview(StyleRecipeRegistry registry, bool useCache)
        {
            var review = new StyleReview { Registry = registry };
            List<PrefabStyleRecipe> recipes = GetRecipes(registry, review);
            RegistryPreviewCache cache = useCache && registry != null
                ? s_previewCaches.GetValue(registry, _ => new RegistryPreviewCache())
                : null;
            StyleInspectionSnapshot snapshot = cache != null ? new StyleInspectionSnapshot(recipes) : null;
            cache?.Retain(recipes);
            var consumers = new ConsumerInspectionBatch();
            var pending = new List<PendingConsumerReview>();

            foreach (PrefabStyleRecipe recipe in recipes)
            {
                RecipePreviewCache entry = cache?.GetEntry(recipe);
                bool ownerSaved = !EditorUtility.IsDirty(recipe.OwnerPrefab);
                bool canCacheOwner = entry != null && ownerSaved
                    && StylePrefabInspectionPolicy.CanReuse(recipe.OwnerPrefab);
                string ownerKey = canCacheOwner ? snapshot.GetOwnerKey(recipe) : null;
                OwnerInspection owner = canCacheOwner && entry.OwnerKey == ownerKey
                    ? entry.Owner
                    : null;
                if (owner == null)
                {
                    var ownerReview = new StyleReview();
                    bool inspectConsumers = InspectOwner(
                        recipe,
                        ownerReview,
                        false,
                        out List<ManagedTarget> managedTargets);
                    owner = new OwnerInspection(ownerReview, managedTargets, inspectConsumers);
                    if (entry != null)
                    {
                        entry.OwnerKey = ownerKey;
                        entry.Owner = canCacheOwner && ownerReview.CanReuseInspection && ownerReview.Errors.Count == 0
                            ? owner.CopyWithoutWorkCounts()
                            : null;
                    }
                }

                consumers.AddReview(owner.Review);
                if (!owner.InspectConsumers)
                {
                    if (entry != null)
                    {
                        entry.Consumers = null;
                    }

                    continue;
                }

                bool canCacheConsumers = ownerSaved && owner.Review.CanReuseInspection && owner.Review.Errors.Count == 0
                    && !(recipe.ConsumerPrefabs ?? Array.Empty<GameObject>()).Any(EditorUtility.IsDirty);
                string consumerKey = canCacheConsumers && entry != null ? snapshot.GetConsumerKey(recipe) : null;
                if (canCacheConsumers && entry?.Consumers != null && entry.ConsumerKey == consumerKey)
                {
                    consumers.AddReview(entry.Consumers);
                    continue;
                }

                var fragments = new List<StyleReview>();
                InspectConsumers(
                    recipe,
                    recipes,
                    AssetDatabase.GetAssetPath(recipe.OwnerPrefab),
                    owner.ManagedTargets,
                    owner.Review,
                    consumers,
                    fragments);
                if (entry != null)
                {
                    entry.Consumers = null;
                    pending.Add(new PendingConsumerReview(entry, consumerKey, fragments, canCacheConsumers));
                }
            }

            consumers.Inspect(recipes, review);
            foreach (PendingConsumerReview item in pending)
            {
                var summary = new StyleReview();
                foreach (StyleReview fragment in item.Fragments)
                {
                    summary.Append(fragment);
                }

                if (item.CanCache && summary.CanReuseInspection && summary.Errors.Count == 0)
                {
                    item.Entry.ConsumerKey = item.Key;
                    item.Entry.Consumers = summary.CopyWithoutWorkCounts();
                }
            }

            // Approval always observes the current complete dependency set, including
            // live ScriptableObjects. Snapshot keys never replace this final check.
            review.Fingerprint = Fingerprint(registry, review);
            return review;
        }

        private static HashSet<string> GetAffectedOwnerPaths(
            StyleRecipeRegistry registry,
            IReadOnlyCollection<string> changedOwnerPaths,
            StyleAssetDependencySnapshot prefabDependencies)
        {
            var changed = new HashSet<string>(changedOwnerPaths, StringComparer.OrdinalIgnoreCase);
            var affected = new HashSet<string>(changed, StringComparer.OrdinalIgnoreCase);
            // A currently Ready nested owner or Variant can become stale after an upstream save.
            affected.UnionWith(prefabDependencies.FindDependents(
                registry.Recipes.Select(recipe => AssetDatabase.GetAssetPath(recipe.OwnerPrefab)),
                changed));

            return affected;
        }

        private static void InvalidatePreviewOwners(
            StyleRecipeRegistry registry,
            ISet<string> affectedOwners,
            StyleAssetDependencySnapshot prefabDependencies)
        {
            if (!s_previewCaches.TryGetValue(registry, out RegistryPreviewCache cache))
            {
                return;
            }

            foreach (KeyValuePair<PrefabStyleRecipe, RecipePreviewCache> pair in cache.Entries)
            {
                PrefabStyleRecipe recipe = pair.Key;
                if (affectedOwners.Contains(AssetDatabase.GetAssetPath(recipe.OwnerPrefab)))
                {
                    pair.Value.Owner = null;
                    pair.Value.Consumers = null;
                }
            }

            KeyValuePair<PrefabStyleRecipe, RecipePreviewCache>[] cachedConsumers = cache.Entries
                .Where(pair => pair.Value.Consumers != null)
                .ToArray();
            HashSet<string> affectedConsumers = prefabDependencies.FindDependents(
                cachedConsumers.SelectMany(pair => pair.Key.ConsumerPrefabs ?? Array.Empty<GameObject>())
                    .Select(AssetDatabase.GetAssetPath),
                affectedOwners);
            foreach (KeyValuePair<PrefabStyleRecipe, RecipePreviewCache> pair in cachedConsumers)
            {
                if ((pair.Key.ConsumerPrefabs ?? Array.Empty<GameObject>())
                    .Any(consumer => affectedConsumers.Contains(AssetDatabase.GetAssetPath(consumer))))
                {
                    pair.Value.Consumers = null;
                }
            }
        }

        private sealed class RegistryPreviewCache
        {
            public Dictionary<PrefabStyleRecipe, RecipePreviewCache> Entries { get; } = new();

            public RecipePreviewCache GetEntry(PrefabStyleRecipe recipe)
            {
                if (!Entries.TryGetValue(recipe, out RecipePreviewCache entry))
                {
                    entry = new RecipePreviewCache();
                    Entries.Add(recipe, entry);
                }

                return entry;
            }

            public void Retain(IEnumerable<PrefabStyleRecipe> recipes)
            {
                var registered = new HashSet<PrefabStyleRecipe>(recipes);
                foreach (PrefabStyleRecipe recipe in Entries.Keys.Where(recipe => !registered.Contains(recipe)).ToArray())
                {
                    Entries.Remove(recipe);
                }
            }
        }

        private sealed class RecipePreviewCache
        {
            public string OwnerKey { get; set; }
            public OwnerInspection Owner { get; set; }
            public string ConsumerKey { get; set; }
            public StyleReview Consumers { get; set; }
        }

        private sealed class OwnerInspection
        {
            public StyleReview Review { get; }
            public IReadOnlyList<ManagedTarget> ManagedTargets { get; }
            public bool InspectConsumers { get; }

            public OwnerInspection(
                StyleReview review,
                IReadOnlyList<ManagedTarget> managedTargets,
                bool inspectConsumers)
            {
                Review = review;
                ManagedTargets = managedTargets;
                InspectConsumers = inspectConsumers;
            }

            public OwnerInspection CopyWithoutWorkCounts()
            {
                return new OwnerInspection(Review.CopyWithoutWorkCounts(), ManagedTargets, InspectConsumers);
            }
        }

        private sealed class PendingConsumerReview
        {
            public RecipePreviewCache Entry { get; }
            public string Key { get; }
            public IReadOnlyList<StyleReview> Fragments { get; }
            public bool CanCache { get; }

            public PendingConsumerReview(
                RecipePreviewCache entry,
                string key,
                IReadOnlyList<StyleReview> fragments,
                bool canCache)
            {
                Entry = entry;
                Key = key;
                Fragments = fragments;
                CanCache = canCache;
            }
        }
    }
}
