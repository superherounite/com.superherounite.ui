using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Reuses saved asset hashes only while Unity's artifact dependencies remain unchanged.</summary>
    internal sealed class StyleRegistryDependencyFingerprint
    {
        private const string SessionCacheKey = "SuperHeroUnite.UI.DependencyHashes.v1";
        private const int SessionCacheFormatVersion = 1;
        private const int MaxSessionEntries = 4096;
        private const int MaxSessionCharacters = 1024 * 1024;
        private static readonly StyleRegistryDependencyFingerprint s_shared = new();
        private readonly Dictionary<string, string> _dependencyHashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<Object, string> _getAssetPath;
        private readonly Func<uint> _getDependencyVersion;
        private readonly Func<string, string> _getDependencyHash;
        private readonly Func<Object, string> _serializeObject;
        private uint _dependencyVersion;

        static StyleRegistryDependencyFingerprint()
        {
            s_shared.ImportSessionState(SessionState.GetString(SessionCacheKey, string.Empty));
            AssemblyReloadEvents.beforeAssemblyReload += SaveSessionState;
        }

        internal StyleRegistryDependencyFingerprint()
            : this(
                AssetDatabase.GetAssetPath,
                () => AssetDatabase.GlobalArtifactDependencyVersion,
                path => AssetDatabase.GetAssetDependencyHash(path).ToString(),
                target => EditorJsonUtility.ToJson(target))
        {
        }

        internal StyleRegistryDependencyFingerprint(
            Func<Object, string> getAssetPath,
            Func<uint> getDependencyVersion,
            Func<string, string> getDependencyHash,
            Func<Object, string> serializeObject)
        {
            _getAssetPath = getAssetPath;
            _getDependencyVersion = getDependencyVersion;
            _getDependencyHash = getDependencyHash;
            _serializeObject = serializeObject;
        }

        internal static string Capture(StyleRecipeRegistry registry)
        {
            return s_shared.Compute(registry);
        }

        internal static string GetSavedAssetDependencyHash(string path)
        {
            return s_shared.GetDependencyHash(path);
        }

        internal string Compute(StyleRecipeRegistry registry)
        {
            // Always read exact live ScriptableObject state, including unmarked edits.
            // Each tracked path retains its own hash: a recursive registry hash can
            // lag behind an immediately saved owner or consumer Prefab.
            var fingerprint = new StyleDependencyFingerprint(
                _getAssetPath,
                GetDependencyHash,
                _serializeObject);
            var text = new StringBuilder();
            text.AppendLine(typeof(StyleRecipeProcessor).Assembly.ManifestModule.ModuleVersionId.ToString());
            fingerprint.AppendObject(text, registry);
            if (registry != null)
            {
                foreach (PrefabStyleRecipe recipe in registry.Recipes ?? Array.Empty<PrefabStyleRecipe>())
                {
                    fingerprint.AppendObject(text, recipe);
                    if (recipe == null)
                    {
                        continue;
                    }

                    fingerprint.AppendObject(text, recipe.OwnerPrefab);
                    foreach (GameObject consumer in recipe.ConsumerPrefabs ?? Array.Empty<GameObject>())
                    {
                        fingerprint.AppendObject(text, consumer);
                    }

                    foreach (Object dependency in StyleRecipeProcessor.GetRecipeAuthoringDependencies(recipe))
                    {
                        fingerprint.AppendObject(text, dependency);
                    }
                }
            }

            using SHA256 hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        internal string ExportSessionState()
        {
            SynchronizeDependencyVersion();
            if (_dependencyHashes.Count == 0 || _dependencyHashes.Count > MaxSessionEntries)
            {
                return string.Empty;
            }

            var paths = new string[_dependencyHashes.Count];
            _dependencyHashes.Keys.CopyTo(paths, 0);
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            var hashes = new string[paths.Length];
            for (int index = 0; index < paths.Length; index++)
            {
                hashes[index] = _dependencyHashes[paths[index]];
            }

            string serialized = JsonUtility.ToJson(new SessionCache
            {
                FormatVersion = SessionCacheFormatVersion,
                DependencyVersion = _dependencyVersion,
                Paths = paths,
                Hashes = hashes,
            });
            return serialized.Length <= MaxSessionCharacters ? serialized : string.Empty;
        }

        internal void ImportSessionState(string serialized)
        {
            _dependencyHashes.Clear();
            _dependencyVersion = _getDependencyVersion();
            if (string.IsNullOrEmpty(serialized) || serialized.Length > MaxSessionCharacters)
            {
                return;
            }

            SessionCache cache;
            try
            {
                cache = JsonUtility.FromJson<SessionCache>(serialized);
            }
            catch (ArgumentException)
            {
                return;
            }

            if (cache == null || cache.FormatVersion != SessionCacheFormatVersion
                || cache.DependencyVersion != _dependencyVersion
                || cache.Paths == null || cache.Hashes == null
                || cache.Paths.Length != cache.Hashes.Length || cache.Paths.Length > MaxSessionEntries)
            {
                return;
            }

            for (int index = 0; index < cache.Paths.Length; index++)
            {
                string path = cache.Paths[index];
                string hash = cache.Hashes[index];
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(hash) || _dependencyHashes.ContainsKey(path))
                {
                    _dependencyHashes.Clear();
                    return;
                }

                _dependencyHashes.Add(path, hash);
            }
        }

        private static void SaveSessionState()
        {
            string serialized = s_shared.ExportSessionState();
            if (string.IsNullOrEmpty(serialized))
            {
                SessionState.EraseString(SessionCacheKey);
                return;
            }

            SessionState.SetString(SessionCacheKey, serialized);
        }

        private string GetDependencyHash(string path)
        {
            SynchronizeDependencyVersion();
            if (!_dependencyHashes.TryGetValue(path, out string dependencyHash))
            {
                dependencyHash = _getDependencyHash(path);
                _dependencyHashes.Add(path, dependencyHash);
            }

            return dependencyHash;
        }

        private void SynchronizeDependencyVersion()
        {
            uint dependencyVersion = _getDependencyVersion();
            if (_dependencyVersion != dependencyVersion)
            {
                _dependencyHashes.Clear();
                _dependencyVersion = dependencyVersion;
            }
        }

        [Serializable]
        private sealed class SessionCache
        {
            public int FormatVersion;
            public uint DependencyVersion;
            public string[] Paths;
            public string[] Hashes;
        }
    }
}
