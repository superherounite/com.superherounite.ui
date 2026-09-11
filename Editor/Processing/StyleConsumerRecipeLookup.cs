using System;
using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Shares recipe and target lookups only while one loaded consumer is inspected.</summary>
    internal sealed class StyleConsumerRecipeLookup
    {
        private readonly Dictionary<string, List<PrefabStyleRecipe>> _recipesByOwner = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _registeredPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly StyleRecipeRelationships _relationships;
        private readonly Dictionary<string, TargetSource> _targetSources = new(StringComparer.Ordinal);
        private readonly Func<GlobalObjectId, IReadOnlyCollection<string>> _getSourceKeys;

        internal StyleConsumerRecipeLookup(
            IReadOnlyList<PrefabStyleRecipe> recipes,
            Func<GlobalObjectId, IReadOnlyCollection<string>> getSourceKeys)
            : this(recipes, AssetDatabase.GetAssetPath, getSourceKeys)
        {
        }

        internal StyleConsumerRecipeLookup(
            IReadOnlyList<PrefabStyleRecipe> recipes,
            Func<Object, string> getAssetPath,
            Func<GlobalObjectId, IReadOnlyCollection<string>> getSourceKeys)
            : this(recipes, getAssetPath, getSourceKeys, new StyleRecipeRelationships(recipes))
        {
        }

        internal StyleConsumerRecipeLookup(
            IReadOnlyList<PrefabStyleRecipe> recipes,
            Func<Object, string> getAssetPath,
            Func<GlobalObjectId, IReadOnlyCollection<string>> getSourceKeys,
            StyleRecipeRelationships relationships)
        {
            _getSourceKeys = getSourceKeys;
            _relationships = relationships;
            foreach (PrefabStyleRecipe recipe in recipes ?? Array.Empty<PrefabStyleRecipe>())
            {
                if (recipe == null)
                {
                    continue;
                }

                string ownerPath = getAssetPath(recipe.OwnerPrefab);
                if (!_recipesByOwner.TryGetValue(ownerPath, out List<PrefabStyleRecipe> owners))
                {
                    owners = new List<PrefabStyleRecipe>();
                    _recipesByOwner.Add(ownerPath, owners);
                }

                owners.Add(recipe);
                _registeredPaths.Add(ownerPath);
                foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
                {
                    _registeredPaths.Add(getAssetPath(consumer));
                }
            }
        }

        internal IReadOnlyList<PrefabStyleRecipe> GetOwners(string path)
        {
            return _recipesByOwner.TryGetValue(path, out List<PrefabStyleRecipe> owners)
                ? owners
                : Array.Empty<PrefabStyleRecipe>();
        }

        internal bool IsRegisteredPrefab(string path)
        {
            return _registeredPaths.Contains(path);
        }

        internal bool IsSpecializationOf(PrefabStyleRecipe recipe, PrefabStyleRecipe baseRecipe)
        {
            return _relationships.IsSpecializationOf(recipe, baseRecipe);
        }

        internal bool TargetsMatch(
            PrefabTargetReference target,
            IReadOnlyCollection<string> baseSourceKeys,
            string baseDisplayPath,
            string baseComponentType)
        {
            if (target == null || string.IsNullOrEmpty(target.GlobalObjectId))
            {
                return false;
            }

            if (!_targetSources.TryGetValue(target.GlobalObjectId, out TargetSource source))
            {
                bool valid = GlobalObjectId.TryParse(target.GlobalObjectId, out GlobalObjectId targetId);
                source = new TargetSource(valid, valid ? _getSourceKeys(targetId) : Array.Empty<string>());
                _targetSources.Add(target.GlobalObjectId, source);
            }

            if (!source.Valid)
            {
                return false;
            }

            foreach (string key in baseSourceKeys)
            {
                if (source.Keys.Contains(key))
                {
                    return true;
                }
            }

            return string.Equals(GetRelativeDisplayPath(target.DisplayPath), GetRelativeDisplayPath(baseDisplayPath), StringComparison.Ordinal)
                && string.Equals(target.ComponentType, baseComponentType, StringComparison.Ordinal);
        }

        private static string GetRelativeDisplayPath(string displayPath)
        {
            int separator = displayPath?.IndexOf('/') ?? -1;
            return separator < 0 ? string.Empty : displayPath.Substring(separator + 1);
        }

        private sealed class TargetSource
        {
            public bool Valid { get; }
            public HashSet<string> Keys { get; }

            public TargetSource(bool valid, IReadOnlyCollection<string> keys)
            {
                Valid = valid;
                Keys = new HashSet<string>(keys, StringComparer.Ordinal);
            }
        }
    }
}
