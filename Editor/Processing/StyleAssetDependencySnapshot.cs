using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;

namespace SuperHeroUnite.UI.Editor
{
    /// <summary>Shares asset dependency reads while planning before any Prefab save.</summary>
    internal sealed class StyleAssetDependencySnapshot
    {
        private readonly Dictionary<string, string[]> _directDependencies = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _directClosures = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _dependencies = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, string[]> _getDirectDependencies;
        private readonly Func<string, string[]> _getDependencies;
        private readonly Func<string[], string[]> _getBatchDependencies;

        internal StyleAssetDependencySnapshot()
            : this(
                path => AssetDatabase.GetDependencies(path, false),
                path => AssetDatabase.GetDependencies(path, true),
                paths => AssetDatabase.GetDependencies(paths, true))
        {
        }

        internal StyleAssetDependencySnapshot(
            Func<string, string[]> getDirectDependencies,
            Func<string, string[]> getDependencies,
            Func<string[], string[]> getBatchDependencies)
        {
            _getDirectDependencies = getDirectDependencies;
            _getDependencies = getDependencies;
            _getBatchDependencies = getBatchDependencies;
        }

        internal string[] GetDependencies(string path)
        {
            if (!_dependencies.TryGetValue(path, out string[] dependencies))
            {
                dependencies = _getDependencies(path);
                _dependencies.Add(path, dependencies);
            }

            return dependencies;
        }

        internal HashSet<string> FindDependents(IEnumerable<string> paths, ISet<string> targets)
        {
            var dependents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (targets.Count == 0)
            {
                return dependents;
            }

            var targetPaths = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unlikely = new List<string>();
            foreach (string path in paths)
            {
                if (!visited.Add(path))
                {
                    continue;
                }

                if (targetPaths.Contains(path))
                {
                    dependents.Add(path);
                }
                else if (GetDirectClosure(path).Any(targetPaths.Contains))
                {
                    if (GetDependencies(path).Any(targetPaths.Contains))
                    {
                        dependents.Add(path);
                    }
                }
                else
                {
                    unlikely.Add(path);
                }
            }

            // Native recursive dependencies can include assets missing from a direct traversal.
            // The graph only groups candidates; both groups require authoritative native checks.
            if (unlikely.Count > 0 && _getBatchDependencies(unlikely.ToArray()).Any(targetPaths.Contains))
            {
                foreach (string path in unlikely)
                {
                    if (GetDependencies(path).Any(targetPaths.Contains))
                    {
                        dependents.Add(path);
                    }
                }
            }

            return dependents;
        }

        private string[] GetDirectClosure(string path)
        {
            if (_directClosures.TryGetValue(path, out string[] cached))
            {
                return cached;
            }

            var dependencies = new List<string> { path };
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
            var pending = new Queue<string>();
            pending.Enqueue(path);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                if (!_directDependencies.TryGetValue(current, out string[] directDependencies))
                {
                    directDependencies = _getDirectDependencies(current);
                    _directDependencies.Add(current, directDependencies);
                }

                foreach (string dependency in directDependencies)
                {
                    if (visited.Add(dependency))
                    {
                        dependencies.Add(dependency);
                        pending.Enqueue(dependency);
                    }
                }
            }

            string[] result = dependencies.ToArray();
            _directClosures.Add(path, result);
            return result;
        }
    }
}
