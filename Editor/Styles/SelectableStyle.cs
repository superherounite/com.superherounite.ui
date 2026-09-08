using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Defines the Color Tint transition values shared by uGUI Selectables.</summary>
    [CreateAssetMenu(fileName = "Selectable Style", menuName = "Super Hero UI/Styles/Selectable Style")]
    public sealed class SelectableStyle : ScriptableObject
    {
        [SerializeField] private ColorToken _normal;
        [SerializeField] private ColorToken _highlighted;
        [SerializeField] private ColorToken _pressed;
        [SerializeField] private ColorToken _selected;
        [SerializeField] private ColorToken _disabled;
        [SerializeField, Min(0f)] private float _colorMultiplier = 1f;
        [SerializeField, Min(0f)] private float _fadeDuration = 0.1f;

        public ColorToken Normal => _normal;
        public ColorToken Highlighted => _highlighted;
        public ColorToken Pressed => _pressed;
        public ColorToken Selected => _selected;
        public ColorToken Disabled => _disabled;
        public float ColorMultiplier => _colorMultiplier;
        public float FadeDuration => _fadeDuration;
    }
}
