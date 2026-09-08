using UnityEngine;
using UnityEngine.UI;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Defines the authored properties that a recipe may bake into a uGUI Image.</summary>
    [CreateAssetMenu(fileName = "Image Style", menuName = "Super Hero UI/Styles/Image Style")]
    public sealed class ImageStyle : ScriptableObject
    {
        [SerializeField] private ColorToken _tint;

        [Header("Optional Owned Properties")]
        [SerializeField] private bool _ownsSprite;
        [SerializeField] private Sprite _sprite;
        [SerializeField] private bool _ownsType;
        [SerializeField] private Image.Type _type = Image.Type.Simple;
        [SerializeField] private bool _ownsPreserveAspect;
        [SerializeField] private bool _preserveAspect;
        [SerializeField] private bool _ownsFillCenter;
        [SerializeField] private bool _fillCenter = true;
        [SerializeField] private bool _ownsPixelsPerUnitMultiplier;
        [SerializeField, Min(0.01f)] private float _pixelsPerUnitMultiplier = 1f;

        public ColorToken Tint => _tint;
        public bool OwnsSprite => _ownsSprite;
        public Sprite Sprite => _sprite;
        public bool OwnsType => _ownsType;
        public Image.Type Type => _type;
        public bool OwnsPreserveAspect => _ownsPreserveAspect;
        public bool PreserveAspect => _preserveAspect;
        public bool OwnsFillCenter => _ownsFillCenter;
        public bool FillCenter => _fillCenter;
        public bool OwnsPixelsPerUnitMultiplier => _ownsPixelsPerUnitMultiplier;
        public float PixelsPerUnitMultiplier => _pixelsPerUnitMultiplier;
    }
}
