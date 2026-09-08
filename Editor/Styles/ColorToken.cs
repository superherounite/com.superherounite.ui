using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Defines a reusable color without requiring a code-defined token.</summary>
    [CreateAssetMenu(fileName = "Color Token", menuName = "Super Hero UI/Styles/Color Token")]
    public sealed class ColorToken : ScriptableObject
    {
        [SerializeField] private Color _value = Color.white;

        public Color Value => _value;
    }
}
