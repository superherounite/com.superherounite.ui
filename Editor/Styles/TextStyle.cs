using TMPro;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Defines typography values that a recipe may bake into a TextMeshPro UI component.</summary>
    [CreateAssetMenu(fileName = "Text Style", menuName = "Super Hero UI/Styles/Text Style")]
    public sealed class TextStyle : ScriptableObject
    {
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private ColorToken _color;
        [SerializeField, Min(0f)] private float _fontSize = 14f;
        [SerializeField] private FontStyles _fontStyle = FontStyles.Normal;
        [SerializeField] private bool _enableAutoSizing;
        [SerializeField, Min(0f)] private float _fontSizeMin = 8f;
        [SerializeField, Min(0f)] private float _fontSizeMax = 40f;
        [SerializeField] private float _characterSpacing;
        [SerializeField] private float _wordSpacing;
        [SerializeField] private float _lineSpacing;
        [SerializeField] private float _paragraphSpacing;

        public TMP_FontAsset Font => _font;
        public ColorToken Color => _color;
        public float FontSize => _fontSize;
        public FontStyles FontStyle => _fontStyle;
        public bool EnableAutoSizing => _enableAutoSizing;
        public float FontSizeMin => _fontSizeMin;
        public float FontSizeMax => _fontSizeMax;
        public float CharacterSpacing => _characterSpacing;
        public float WordSpacing => _wordSpacing;
        public float LineSpacing => _lineSpacing;
        public float ParagraphSpacing => _paragraphSpacing;
    }
}
