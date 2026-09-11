using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Captures dependency keys for one read-only inspection, before any Prefab writes.</summary>
    internal sealed class StyleInspectionSnapshot
    {
        private readonly Dictionary<PrefabStyleRecipe, List<PrefabStyleRecipe>> _specializations = new();
        private readonly Dictionary<PrefabStyleRecipe, string> _ownerKeys = new();
        private readonly Dictionary<PrefabStyleRecipe, string> _consumerKeys = new();

        internal StyleDependencyFingerprint Fingerprint { get; } = new(
            AssetDatabase.GetAssetPath,
            StyleRegistryDependencyFingerprint.GetSavedAssetDependencyHash,
            target => EditorJsonUtility.ToJson(target));

        internal StyleInspectionSnapshot(IReadOnlyList<PrefabStyleRecipe> recipes)
        {
            var relationships = new StyleRecipeRelationships(recipes);
            foreach (PrefabStyleRecipe recipe in recipes ?? Array.Empty<PrefabStyleRecipe>())
            {
                PrefabStyleRecipe baseRecipe = relationships.GetBaseRecipe(recipe);
                if (baseRecipe == null)
                {
                    continue;
                }

                if (!_specializations.TryGetValue(
                    baseRecipe,
                    out List<PrefabStyleRecipe> specializations))
                {
                    specializations = new List<PrefabStyleRecipe>();
                    _specializations.Add(baseRecipe, specializations);
                }

                specializations.Add(recipe);
            }
        }

        internal string GetOwnerKey(PrefabStyleRecipe recipe)
        {
            if (recipe == null)
            {
                return Hash("<missing>" + Environment.NewLine);
            }

            if (_ownerKeys.TryGetValue(recipe, out string ownerKey))
            {
                return ownerKey;
            }

            var text = new StringBuilder();
            text.AppendLine("<recipe>");
            text.AppendLine(AssetDatabase.GetAssetPath(recipe));
            // The recipe's recursive asset hash also follows consumers, which do not affect owner values.
            text.AppendLine(Fingerprint.GetSerializedDigest(recipe));
            text.AppendLine("<owner-prefab>");
            Fingerprint.AppendObject(text, recipe.OwnerPrefab);
            text.AppendLine("<authoring-dependencies>");
            foreach (Object dependency in StyleRecipeProcessor.GetRecipeAuthoringDependencies(recipe))
            {
                Fingerprint.AppendObject(text, dependency);
            }

            text.AppendLine("<prefab-stage>");
            text.AppendLine(StyleRecipeProcessor.GetUnsavedTrackedStageError(recipe));
            ownerKey = Hash(text.ToString());
            _ownerKeys.Add(recipe, ownerKey);
            return ownerKey;
        }

        internal string GetConsumerKey(PrefabStyleRecipe recipe)
        {
            if (recipe == null)
            {
                return Hash("<missing>" + Environment.NewLine);
            }

            if (_consumerKeys.TryGetValue(recipe, out string consumerKey))
            {
                return consumerKey;
            }

            var text = new StringBuilder();
            text.AppendLine("<owner>");
            text.AppendLine(GetOwnerKey(recipe));
            text.AppendLine("<consumer-prefabs>");
            foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
            {
                Fingerprint.AppendObject(text, consumer);
            }

            text.AppendLine("<specializations>");
            // Ancestor consumers can contain descendant Variants even when intermediate recipes have no consumers.
            var visited = new HashSet<PrefabStyleRecipe> { recipe };
            var pending = new Queue<PrefabStyleRecipe>();
            pending.Enqueue(recipe);
            while (pending.Count > 0)
            {
                if (!_specializations.TryGetValue(pending.Dequeue(), out List<PrefabStyleRecipe> specializations))
                {
                    continue;
                }

                foreach (PrefabStyleRecipe specialization in specializations)
                {
                    if (!visited.Add(specialization))
                    {
                        continue;
                    }

                    text.AppendLine(GetOwnerKey(specialization));
                    pending.Enqueue(specialization);
                }
            }

            consumerKey = Hash(text.ToString());
            _consumerKeys.Add(recipe, consumerKey);
            return consumerKey;
        }

        private static string Hash(string value)
        {
            using SHA256 hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }
}
