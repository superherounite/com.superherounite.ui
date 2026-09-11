using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    internal enum StyleRecipeRelationship
    {
        Owner,
        Inherited,
        Consumer
    }

    internal readonly struct StyleRecipeMatch
    {
        public PrefabStyleRecipe Recipe { get; }
        public StyleRecipeRelationship Relationship { get; }
        public string PrefabPath { get; }

        public StyleRecipeMatch(
            PrefabStyleRecipe recipe,
            StyleRecipeRelationship relationship,
            string prefabPath)
        {
            Recipe = recipe;
            Relationship = relationship;
            PrefabPath = prefabPath;
        }
    }

    internal sealed class StyleRecipeLookup
    {
        private readonly Func<IReadOnlyList<PrefabStyleRecipe>> _findRecipes;
        private IReadOnlyList<PrefabStyleRecipe> _recipes;

        internal StyleRecipeLookup()
            : this(FindRecipeAssets)
        {
        }

        internal StyleRecipeLookup(Func<IReadOnlyList<PrefabStyleRecipe>> findRecipes)
        {
            _findRecipes = findRecipes ?? throw new ArgumentNullException(nameof(findRecipes));
        }

        internal IReadOnlyList<StyleRecipeMatch> Find(Object target)
        {
            string prefabPath = GetPrefabPath(target);
            if (string.IsNullOrEmpty(prefabPath))
            {
                return Array.Empty<StyleRecipeMatch>();
            }

            HashSet<string> sourcePaths = GetSourcePaths(prefabPath);
            _recipes ??= new List<PrefabStyleRecipe>(_findRecipes() ?? Array.Empty<PrefabStyleRecipe>());

            var matches = new List<StyleRecipeMatch>();
            var visitedRecipes = new HashSet<PrefabStyleRecipe>();
            foreach (PrefabStyleRecipe recipe in _recipes)
            {
                if (recipe == null || !visitedRecipes.Add(recipe))
                {
                    continue;
                }

                string ownerPath = AssetDatabase.GetAssetPath(recipe.OwnerPrefab);
                if (string.Equals(ownerPath, prefabPath, StringComparison.Ordinal))
                {
                    matches.Add(new StyleRecipeMatch(recipe, StyleRecipeRelationship.Owner, prefabPath));
                }
                else if (sourcePaths.Contains(ownerPath))
                {
                    matches.Add(new StyleRecipeMatch(recipe, StyleRecipeRelationship.Inherited, ownerPath));
                }
                else if (IsRegisteredConsumer(recipe, prefabPath))
                {
                    matches.Add(new StyleRecipeMatch(recipe, StyleRecipeRelationship.Consumer, prefabPath));
                }
            }

            matches.Sort(CompareMatches);
            return matches;
        }

        internal void Invalidate()
        {
            _recipes = null;
        }

        internal static bool HasPrefabContext(Object target)
        {
            return !string.IsNullOrEmpty(GetPrefabPath(target));
        }

        private static IReadOnlyList<PrefabStyleRecipe> FindRecipeAssets()
        {
            string[] recipeGuids = AssetDatabase.FindAssets("t:PrefabStyleRecipe");
            Array.Sort(recipeGuids, StringComparer.Ordinal);
            var recipes = new List<PrefabStyleRecipe>(recipeGuids.Length);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in recipeGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!paths.Add(path))
                {
                    continue;
                }

                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is PrefabStyleRecipe recipe)
                    {
                        recipes.Add(recipe);
                    }
                }
            }

            return recipes;
        }

        private static string GetPrefabPath(Object target)
        {
            if (target == null)
            {
                return null;
            }

            GameObject gameObject = target switch
            {
                GameObject selectedObject => selectedObject,
                Component selectedComponent => selectedComponent.gameObject,
                _ => null
            };
            if (gameObject == null)
            {
                return null;
            }

            PrefabStage stage = PrefabStageUtility.GetPrefabStage(gameObject);
            GameObject stageRoot = stage != null && stage.IsPartOfPrefabContents(gameObject)
                ? stage.prefabContentsRoot
                : null;
            GameObject nearestInstance = GetNearestInstance(gameObject, stageRoot);
            string path;
            if (stageRoot != null)
            {
                // A Variant's stage root is an instance of its base, but the opened Variant owns this context.
                path = nearestInstance != null && nearestInstance != stageRoot
                    ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearestInstance)
                    : stage.assetPath;
            }
            else if (EditorUtility.IsPersistent(gameObject))
            {
                GameObject assetRoot = gameObject.transform.root.gameObject;
                path = nearestInstance != null && nearestInstance != assetRoot
                    ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearestInstance)
                    : AssetDatabase.GetAssetPath(gameObject);
            }
            else
            {
                path = nearestInstance != null
                    ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearestInstance)
                    : null;
            }

            return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
        }

        private static GameObject GetNearestInstance(GameObject gameObject, GameObject stageRoot)
        {
            // Added overrides have no prefab correspondence yet; their nearest linked parent supplies the context.
            for (Transform current = gameObject.transform; current != null; current = current.parent)
            {
                GameObject instance = PrefabUtility.GetNearestPrefabInstanceRoot(current.gameObject);
                if (instance != null)
                {
                    return instance;
                }

                if (current.gameObject == stageRoot)
                {
                    break;
                }
            }

            return null;
        }

        private static HashSet<string> GetSourcePaths(string prefabPath)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var current = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            while (current != null)
            {
                current = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (current == null)
                {
                    break;
                }

                string sourcePath = AssetDatabase.GetAssetPath(current);
                if (string.IsNullOrEmpty(sourcePath)
                    || string.Equals(sourcePath, prefabPath, StringComparison.Ordinal)
                    || !paths.Add(sourcePath))
                {
                    break;
                }
            }

            return paths;
        }

        private static bool IsRegisteredConsumer(PrefabStyleRecipe recipe, string prefabPath)
        {
            foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
            {
                if (consumer != null
                    && string.Equals(AssetDatabase.GetAssetPath(consumer), prefabPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareMatches(StyleRecipeMatch left, StyleRecipeMatch right)
        {
            int relationshipOrder = left.Relationship.CompareTo(right.Relationship);
            if (relationshipOrder != 0)
            {
                return relationshipOrder;
            }

            int recipeOrder = string.Compare(
                AssetDatabase.GetAssetPath(left.Recipe),
                AssetDatabase.GetAssetPath(right.Recipe),
                StringComparison.Ordinal);
            if (recipeOrder != 0)
            {
                return recipeOrder;
            }

            int prefabOrder = string.Compare(left.PrefabPath, right.PrefabPath, StringComparison.Ordinal);
            if (prefabOrder != 0)
            {
                return prefabOrder;
            }

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(left.Recipe, out string _, out long leftId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(right.Recipe, out string _, out long rightId);
            return leftId.CompareTo(rightId);
        }
    }
}
