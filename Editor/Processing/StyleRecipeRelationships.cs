using System;
using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Resolves recipe inheritance from the current Prefab sources without editing authoring assets.</summary>
    internal sealed class StyleRecipeRelationships
    {
        private readonly Dictionary<PrefabStyleRecipe, PrefabStyleRecipe> _baseRecipes = new();
        private readonly Dictionary<PrefabStyleRecipe, HashSet<PrefabStyleRecipe>> _ancestors = new();
        private readonly Dictionary<PrefabStyleRecipe, GameObject> _ownerPrefabs = new();
        private readonly Dictionary<GameObject, PrefabStyleRecipe> _owners = new();
        private readonly HashSet<GameObject> _ambiguousOwners = new();
        private readonly Dictionary<GameObject, GameObject> _sources = new();
        private readonly Func<GameObject, GameObject> _getPrefabSource;

        internal StyleRecipeRelationships(IReadOnlyList<PrefabStyleRecipe> recipes)
            : this(recipes, GetPrefabSource)
        {
        }

        internal StyleRecipeRelationships(
            IReadOnlyList<PrefabStyleRecipe> recipes,
            Func<GameObject, GameObject> getPrefabSource)
        {
            _getPrefabSource = getPrefabSource;
            foreach (PrefabStyleRecipe recipe in recipes ?? Array.Empty<PrefabStyleRecipe>())
            {
                if (recipe == null || _ownerPrefabs.ContainsKey(recipe))
                {
                    continue;
                }

                _ownerPrefabs.Add(recipe, recipe.OwnerPrefab);
                if (recipe.BaseRecipe != null)
                {
                    _baseRecipes.Add(recipe, recipe.BaseRecipe);
                }

                if (recipe.OwnerPrefab == null)
                {
                    continue;
                }

                if (_owners.TryGetValue(recipe.OwnerPrefab, out PrefabStyleRecipe existing)
                    && existing != recipe)
                {
                    _ambiguousOwners.Add(recipe.OwnerPrefab);
                }
                else
                {
                    _owners[recipe.OwnerPrefab] = recipe;
                }
            }
        }

        internal PrefabStyleRecipe GetBaseRecipe(PrefabStyleRecipe recipe)
        {
            if (recipe == null || !_ownerPrefabs.TryGetValue(recipe, out GameObject owner))
            {
                return null;
            }

            if (_baseRecipes.TryGetValue(recipe, out PrefabStyleRecipe baseRecipe))
            {
                return baseRecipe;
            }

            if (owner != null && !_ambiguousOwners.Contains(owner))
            {
                var visited = new HashSet<GameObject>();
                GameObject current = owner;
                while (current != null && visited.Add(current))
                {
                    if (!_sources.TryGetValue(current, out GameObject source))
                    {
                        source = _getPrefabSource(current);
                        _sources.Add(current, source);
                    }

                    if (source == null || visited.Contains(source) || _ambiguousOwners.Contains(source))
                    {
                        break;
                    }

                    if (_owners.TryGetValue(source, out baseRecipe))
                    {
                        break;
                    }

                    current = source;
                }
            }

            _baseRecipes.Add(recipe, baseRecipe);
            return baseRecipe;
        }

        internal bool IsSpecializationOf(PrefabStyleRecipe recipe, PrefabStyleRecipe baseRecipe)
        {
            if (recipe == null || baseRecipe == null || recipe == baseRecipe)
            {
                return false;
            }

            if (!_ancestors.TryGetValue(recipe, out HashSet<PrefabStyleRecipe> ancestors))
            {
                ancestors = new HashSet<PrefabStyleRecipe>();
                PrefabStyleRecipe current = GetBaseRecipe(recipe);
                while (current != null && current != recipe && ancestors.Add(current))
                {
                    current = GetBaseRecipe(current);
                }

                _ancestors.Add(recipe, ancestors);
            }

            return ancestors.Contains(baseRecipe);
        }

        private static GameObject GetPrefabSource(GameObject owner)
        {
            return EditorUtility.IsPersistent(owner) && PrefabUtility.IsPartOfPrefabAsset(owner)
                ? PrefabUtility.GetCorrespondingObjectFromSource(owner)
                : null;
        }
    }
}
