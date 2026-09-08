using System;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Opts a project into automatic validation for an explicit set of style recipes.</summary>
    [CreateAssetMenu(fileName = "Style Recipe Registry", menuName = "Super Hero UI/Recipes/Style Recipe Registry")]
    public sealed class StyleRecipeRegistry : ScriptableObject
    {
        [SerializeField] private PrefabStyleRecipe[] _recipes = Array.Empty<PrefabStyleRecipe>();
        [SerializeField] private bool _validateBeforePlay = true;
        [SerializeField] private bool _validateBeforeBuild = true;

        public PrefabStyleRecipe[] Recipes => _recipes;
        public bool ValidateBeforePlay => _validateBeforePlay;
        public bool ValidateBeforeBuild => _validateBeforeBuild;
    }
}
