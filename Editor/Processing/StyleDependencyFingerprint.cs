using System;
using System.Collections.Generic;
using System.Text;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Caches shared asset reads during one dependency fingerprint operation.</summary>
    internal sealed class StyleDependencyFingerprint
    {
        private readonly Dictionary<string, string> _dependencyHashes = new(StringComparer.OrdinalIgnoreCase);
        // Values are compact serialized-state digests, never the original JSON payloads.
        private readonly Dictionary<Object, string> _serializedObjects = new();
        private readonly Func<Object, string> _getAssetPath;
        private readonly Func<string, string> _getDependencyHash;
        private readonly Func<Object, string> _serializeObject;

        internal StyleDependencyFingerprint()
            : this(
                AssetDatabase.GetAssetPath,
                path => AssetDatabase.GetAssetDependencyHash(path).ToString(),
                target => EditorJsonUtility.ToJson(target))
        {
        }

        internal StyleDependencyFingerprint(
            Func<Object, string> getAssetPath,
            Func<string, string> getDependencyHash,
            Func<Object, string> serializeObject)
        {
            _getAssetPath = getAssetPath;
            _getDependencyHash = getDependencyHash;
            _serializeObject = serializeObject;
        }

        internal void AppendObject(StringBuilder text, Object target)
        {
            if (target == null)
            {
                text.AppendLine("<missing>");
                return;
            }

            string path = _getAssetPath(target);
            text.AppendLine(path);
            if (!string.IsNullOrEmpty(path))
            {
                if (!_dependencyHashes.TryGetValue(path, out string dependencyHash))
                {
                    dependencyHash = _getDependencyHash(path);
                    _dependencyHashes.Add(path, dependencyHash);
                }

                text.AppendLine(dependencyHash);
            }

            if (target is ScriptableObject scriptableObject)
            {
                text.AppendLine(GetSerializedDigest(scriptableObject));
            }
        }

        internal string GetSerializedDigest(ScriptableObject target)
        {
            if (target == null)
            {
                return "<missing>";
            }

            if (!_serializedObjects.TryGetValue(target, out string serializedDigest))
            {
                string serializedJson = _serializeObject(target) ?? string.Empty;
                // Native hashing avoids copying and hashing large TMP payloads in managed code.
                serializedDigest = "json-hash128:" + Hash128.Compute(serializedJson);
                _serializedObjects.Add(target, serializedDigest);
            }

            return serializedDigest;
        }
    }
}
