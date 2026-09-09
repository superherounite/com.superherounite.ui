using System;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Declares the style assets and prefab components owned by one bake recipe.</summary>
    [CreateAssetMenu(fileName = "Prefab Style Recipe", menuName = "Super Hero UI/Recipes/Prefab Style Recipe")]
    public sealed class PrefabStyleRecipe : ScriptableObject
    {
        [Serializable]
        public sealed class GraphicColorBinding
        {
            [SerializeField] private PrefabTargetReference _target = new();
            [SerializeField] private ColorToken _color;

            public PrefabTargetReference Target => _target;
            public ColorToken Color => _color;

            internal void EnsureTargetKind()
            {
                _target ??= new PrefabTargetReference();
                _target.EnsureKind(PrefabTargetKind.Graphic);
            }
        }

        [Serializable]
        public sealed class ImageBinding
        {
            [SerializeField] private PrefabTargetReference _target = new();
            [SerializeField] private ImageStyle _style;

            public PrefabTargetReference Target => _target;
            public ImageStyle Style => _style;

            internal void EnsureTargetKind()
            {
                _target ??= new PrefabTargetReference();
                _target.EnsureKind(PrefabTargetKind.Image);
            }
        }

        [Serializable]
        public sealed class SurfaceBinding
        {
            [SerializeField] private PrefabTargetReference _fillTarget = new();
            [SerializeField] private PrefabTargetReference _outlineTarget = new();
            [SerializeField] private SurfaceStyle _style;

            public PrefabTargetReference FillTarget => _fillTarget;
            public PrefabTargetReference OutlineTarget => _outlineTarget;
            public SurfaceStyle Style => _style;

            internal void EnsureTargetKinds()
            {
                _fillTarget ??= new PrefabTargetReference();
                _fillTarget.EnsureKind(PrefabTargetKind.Image);
                _outlineTarget ??= new PrefabTargetReference();
                _outlineTarget.EnsureKind(PrefabTargetKind.Image);
            }
        }

        [Serializable]
        public sealed class TextBinding
        {
            [SerializeField] private PrefabTargetReference _target = new();
            [SerializeField] private TextStyle _style;

            public PrefabTargetReference Target => _target;
            public TextStyle Style => _style;

            internal void EnsureTargetKind()
            {
                _target ??= new PrefabTargetReference();
                _target.EnsureKind(PrefabTargetKind.Text);
            }
        }

        [Serializable]
        public sealed class SelectableBinding
        {
            [SerializeField] private PrefabTargetReference _target = new();
            [SerializeField] private SelectableStyle _style;

            public PrefabTargetReference Target => _target;
            public SelectableStyle Style => _style;

            internal void EnsureTargetKind()
            {
                _target ??= new PrefabTargetReference();
                _target.EnsureKind(PrefabTargetKind.Selectable);
            }
        }

        [SerializeField] private GameObject _ownerPrefab;
        [SerializeField] private PrefabStyleRecipe _baseRecipe;
        [SerializeField] private GameObject[] _consumerPrefabs = Array.Empty<GameObject>();
        [SerializeField] private GraphicColorBinding[] _graphicColors = Array.Empty<GraphicColorBinding>();
        [SerializeField] private ImageBinding[] _images = Array.Empty<ImageBinding>();
        [SerializeField] private SurfaceBinding[] _surfaces = Array.Empty<SurfaceBinding>();
        [SerializeField] private TextBinding[] _texts = Array.Empty<TextBinding>();
        [SerializeField] private SelectableBinding[] _selectables = Array.Empty<SelectableBinding>();

        public GameObject OwnerPrefab => _ownerPrefab;
        /// <summary>Declares the source recipe whose values this Prefab Variant intentionally specializes.</summary>
        public PrefabStyleRecipe BaseRecipe => _baseRecipe;
        public GameObject[] ConsumerPrefabs => _consumerPrefabs;
        public GraphicColorBinding[] GraphicColors => _graphicColors;
        public ImageBinding[] Images => _images;
        public SurfaceBinding[] Surfaces => _surfaces;
        public TextBinding[] Texts => _texts;
        public SelectableBinding[] Selectables => _selectables;

        private void OnValidate()
        {
            foreach (GraphicColorBinding binding in _graphicColors ?? Array.Empty<GraphicColorBinding>())
            {
                binding?.EnsureTargetKind();
            }

            foreach (ImageBinding binding in _images ?? Array.Empty<ImageBinding>())
            {
                binding?.EnsureTargetKind();
            }

            foreach (SurfaceBinding binding in _surfaces ?? Array.Empty<SurfaceBinding>())
            {
                binding?.EnsureTargetKinds();
            }

            foreach (TextBinding binding in _texts ?? Array.Empty<TextBinding>())
            {
                binding?.EnsureTargetKind();
            }

            foreach (SelectableBinding binding in _selectables ?? Array.Empty<SelectableBinding>())
            {
                binding?.EnsureTargetKind();
            }
        }
    }
}
