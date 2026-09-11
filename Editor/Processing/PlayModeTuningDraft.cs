using System;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    [Serializable]
    internal sealed class PlayModeTuningDraft
    {
        [SerializeField] private string _recipeId;
        [SerializeField] private string _bindingKey;
        [SerializeField] private string _sourceId;
        [SerializeField] private string _recipeJson;
        [SerializeField] private string _label;
        [SerializeField] private bool _isColor;
        [SerializeField] private Color _originalColor;
        [SerializeField] private float _originalFloat;
        [SerializeField] private Color _colorValue;
        [SerializeField] private float _floatValue;

        internal string RecipeId => _recipeId;
        internal string BindingKey => _bindingKey;
        internal string SourceId => _sourceId;
        internal string Label => _label;
        internal bool IsColor => _isColor;
        internal Color ColorValue => _colorValue;
        internal float FloatValue => _floatValue;
        internal Color OriginalColor => _originalColor;
        internal float OriginalFloat => _originalFloat;

        internal PlayModeTuningDraft(PlayModeTuningBinding binding)
        {
            _recipeId = GetAssetId(binding.Recipe);
            _bindingKey = binding.Key;
            _sourceId = GetAssetId(binding.SourceAsset);
            _recipeJson = EditorJsonUtility.ToJson(binding.Recipe);
            _label = $"{binding.Recipe.name} / {binding.Label}";
            _isColor = binding.IsColor;
            if (_isColor)
            {
                _originalColor = binding.SourceColor;
                _colorValue = _originalColor;
            }
            else
            {
                _originalFloat = binding.SourceFloat;
                _floatValue = _originalFloat;
            }
        }

        internal void SetColor(Color value)
        {
            _colorValue = value;
        }

        internal void SetFloat(float value)
        {
            _floatValue = value;
        }

        internal bool TryResolve(out PlayModeTuningBinding binding, out string error)
        {
            binding = null;
            var recipe = ResolveAsset(_recipeId) as PrefabStyleRecipe;
            if (recipe == null || !string.Equals(
                EditorJsonUtility.ToJson(recipe), _recipeJson, StringComparison.Ordinal))
            {
                error = $"{_label}: the Recipe changed or is missing. Discard this draft and tune again.";
                return false;
            }

            if (PlayModeTuningBinding.TryFindBinding(recipe, _bindingKey, out PlayModeTuningBinding candidate)
                && EditorUtility.IsPersistent(candidate.SourceAsset)
                && GetAssetId(candidate.SourceAsset) == _sourceId && candidate.IsColor == _isColor)
            {
                binding = candidate;
                error = null;
                return true;
            }

            error = $"{_label}: the binding, ownership, or source asset changed. Discard this draft and tune again.";
            return false;
        }

        internal static string GetAssetId(Object asset)
        {
            if (asset == null || !EditorUtility.IsPersistent(asset))
            {
                throw new InvalidOperationException("Choose a saved project Recipe and saved style assets.");
            }

            return GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString();
        }

        internal static Object ResolveAsset(string identifier)
        {
            return GlobalObjectId.TryParse(identifier, out GlobalObjectId globalId)
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId)
                : null;
        }
    }
}
