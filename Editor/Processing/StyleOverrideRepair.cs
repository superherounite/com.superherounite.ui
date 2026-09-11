using System;
using System.Globalization;

namespace SuperHeroUnite.UI.Editor
{
    internal sealed class StyleOverrideRepair
    {
        internal string AssetPath { get; }
        internal PrefabTargetResolver.PrefabComponentLocator Locator { get; }
        internal string GlobalObjectId { get; }
        internal string PropertyPath { get; }
        internal string Error { get; }
        internal string Key { get; }
        internal string Description { get; }

        internal StyleOverrideRepair(
            string assetPath,
            PrefabTargetResolver.PrefabComponentLocator locator,
            string globalObjectId,
            string propertyPath,
            string displayPath,
            string error)
        {
            AssetPath = assetPath;
            Locator = locator;
            GlobalObjectId = globalObjectId;
            PropertyPath = propertyPath;
            Error = error;
            Key = assetPath + ":" + NormalizeGlobalObjectId(globalObjectId) + ":" + propertyPath;
            Description = $"{assetPath}/{displayPath} ({locator.ComponentType.Name}): "
                + $"revert {propertyPath} override to inherit the source Prefab value.";
        }

        internal static string NormalizeGlobalObjectId(string globalObjectId)
        {
            if (!UnityEditor.GlobalObjectId.TryParse(globalObjectId, out UnityEditor.GlobalObjectId identifier)
                || identifier.targetObjectId == 0)
            {
                throw new InvalidOperationException("The override target does not have a stable Prefab identity.");
            }

            // Match PlayModeTuningBinding's loaded-scene to imported Prefab identity normalization.
            ulong localId = identifier.identifierType == 2 && identifier.targetPrefabId != 0
                ? (identifier.targetObjectId ^ identifier.targetPrefabId) & 0x7fffffffffffffffUL
                : identifier.targetObjectId;
            return identifier.assetGUID + ":" + localId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
