using System;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Describes the kind of component expected at a serialized prefab target.</summary>
    public enum PrefabTargetKind
    {
        Graphic,
        Image,
        Text,
        Selectable,
    }

    /// <summary>Stores a stable prefab component identity with readable diagnostic metadata.</summary>
    [Serializable]
    public sealed class PrefabTargetReference
    {
        [SerializeField, HideInInspector] private string _globalObjectId = string.Empty;
        [SerializeField, HideInInspector] private string _displayPath = string.Empty;
        [SerializeField, HideInInspector] private string _componentType = string.Empty;
        [SerializeField, HideInInspector] private PrefabTargetKind _expectedKind;

        public string GlobalObjectId => _globalObjectId;
        public string DisplayPath => _displayPath;
        public string ComponentType => _componentType;
        public PrefabTargetKind ExpectedKind => _expectedKind;

        internal void Configure(
            string globalObjectId,
            string displayPath,
            string componentType,
            PrefabTargetKind expectedKind)
        {
            _globalObjectId = globalObjectId ?? string.Empty;
            _displayPath = displayPath ?? string.Empty;
            _componentType = componentType ?? string.Empty;
            _expectedKind = expectedKind;
        }

        internal void EnsureKind(PrefabTargetKind expectedKind)
        {
            _expectedKind = expectedKind;
        }
    }
}
