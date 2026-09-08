using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Composes a required fill and an optional outline from two Image styles.</summary>
    [CreateAssetMenu(fileName = "Surface Style", menuName = "Super Hero UI/Styles/Surface Style")]
    public sealed class SurfaceStyle : ScriptableObject
    {
        [SerializeField] private ImageStyle _fill;
        [SerializeField] private ImageStyle _outline;

        public ImageStyle Fill => _fill;
        public ImageStyle Outline => _outline;
    }
}
