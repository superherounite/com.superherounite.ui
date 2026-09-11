using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using TMPro;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleRegistryDependencyFingerprintTests
    {
        private readonly List<Object> _objects = new();
        private readonly Dictionary<Object, string> _paths = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object target in _objects)
            {
                Object.DestroyImmediate(target);
            }

            _objects.Clear();
            _paths.Clear();
        }

        [TestCase(1)]
        [TestCase(100)]
        public void UnchangedArtifactVersionReusesSavedHashesButReadsLiveAssetsEveryTime(int recipeCount)
        {
            StyleRecipeRegistry registry = CreateRegistry(recipeCount, out ColorToken sharedToken);
            var hashQueries = new List<string>();
            var serialized = new List<Object>();
            var fingerprint = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => 1,
                path =>
                {
                    hashQueries.Add(path);
                    return "complete-recursive-registry-hash";
                },
                target =>
                {
                    serialized.Add(target);
                    return EditorJsonUtility.ToJson(target);
                });

            Assert.That(fingerprint.Compute(registry), Is.Not.Empty);
            int initialHashQueries = hashQueries.Count;
            Assert.That(fingerprint.Compute(registry), Is.Not.Empty);

            Assert.That(initialHashQueries, Is.EqualTo(recipeCount * 2 + 2));
            Assert.That(hashQueries.Count, Is.EqualTo(initialHashQueries));
            Assert.That(serialized.Count(target => target == sharedToken), Is.EqualTo(2));
            Assert.That(serialized.Count, Is.EqualTo((recipeCount + 2) * 2));
        }

        [Test]
        public void AssetReferencedOnlyByUnmarkedLiveEditKeepsItsOwnSavedDependencyHash()
        {
            StyleRecipeRegistry registry = CreateRegistry(1, out ColorToken savedToken);
            ColorToken liveToken = CreateAsset<ColorToken>("OutsideSavedGraph");
            string outsideHash = "outside-before";
            uint dependencyVersion = 1;
            var hashes = new List<string>();
            var fingerprint = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => dependencyVersion,
                path =>
                {
                    hashes.Add(path);
                    return path == _paths[liveToken] ? outsideHash : "registry-hash";
                },
                target => EditorJsonUtility.ToJson(target));
            SetPrivateField(registry.Recipes[0].GraphicColors[0], "_color", liveToken);

            string before = fingerprint.Compute(registry);
            outsideHash = "outside-after";
            dependencyVersion++;
            string after = fingerprint.Compute(registry);

            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(hashes.Count(path => path == _paths[liveToken]), Is.EqualTo(2));
            Assert.That(hashes, Does.Not.Contain(_paths[savedToken]));
        }

        [Test]
        public void UnmarkedLiveTokenAndFontFallbackEditsInvalidateExactDigest()
        {
            StyleRecipeRegistry registry = CreateRegistry(1, out ColorToken token);
            TMP_FontAsset font = CreateAsset<TMP_FontAsset>("Font");
            TMP_FontAsset fallback = CreateAsset<TMP_FontAsset>("Fallback");
            font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            TextStyle style = CreateAsset<TextStyle>("TextStyle");
            SetPrivateField(style, "_font", font);
            SetPrivateField(style, "_color", token);
            var binding = new PrefabStyleRecipe.TextBinding();
            SetPrivateField(binding, "_style", style);
            SetPrivateField(registry.Recipes[0], "_texts", new[] { binding });
            var fingerprint = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => 1,
                path => "unchanged-saved-graph",
                target => EditorJsonUtility.ToJson(target));
            string original = fingerprint.Compute(registry);
            int tokenDirtyCount = EditorUtility.GetDirtyCount(token);
            int fontDirtyCount = EditorUtility.GetDirtyCount(font);

            SetPrivateField(token, "_value", Color.magenta);
            Assert.That(EditorUtility.GetDirtyCount(token), Is.EqualTo(tokenDirtyCount));
            string changedToken = fingerprint.Compute(registry);
            Assert.That(changedToken, Is.Not.EqualTo(original));

            font.fallbackFontAssetTable.Add(fallback);
            Assert.That(EditorUtility.GetDirtyCount(font), Is.EqualTo(fontDirtyCount));
            Assert.That(fingerprint.Compute(registry), Is.Not.EqualTo(changedToken));
            font.fallbackFontAssetTable.Clear();
            Assert.That(fingerprint.Compute(registry), Is.EqualTo(changedToken));
        }

        [Test]
        public void ChangedSavedGraphInvalidatesDigestWithoutLiveObjectEdits()
        {
            StyleRecipeRegistry registry = CreateRegistry(1, out _);
            string savedHash = "before-worktree-switch";
            uint dependencyVersion = 1;
            var fingerprint = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => dependencyVersion,
                path => savedHash,
                target => EditorJsonUtility.ToJson(target));
            string before = fingerprint.Compute(registry);

            savedHash = "after-worktree-switch";
            dependencyVersion++;

            Assert.That(fingerprint.Compute(registry), Is.Not.EqualTo(before));
        }

        [Test]
        public void SessionRoundtripReusesSavedHashesButReadsFreshLiveState()
        {
            StyleRecipeRegistry registry = CreateRegistry(1, out ColorToken token);
            var original = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => 1,
                path => "saved-hash:" + path,
                target => EditorJsonUtility.ToJson(target));
            string before = original.Compute(registry);
            string savedSession = original.ExportSessionState();
            Assert.That(savedSession, Is.Not.Empty);
            int hashQueries = 0;
            int liveReads = 0;
            var restored = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => 1,
                path =>
                {
                    hashQueries++;
                    return "saved-hash:" + path;
                },
                target =>
                {
                    liveReads++;
                    return EditorJsonUtility.ToJson(target);
                });

            restored.ImportSessionState(savedSession);

            Assert.That(restored.Compute(registry), Is.EqualTo(before));
            Assert.That(hashQueries, Is.Zero);
            Assert.That(liveReads, Is.EqualTo(3));
            SetPrivateField(token, "_value", Color.cyan);
            Assert.That(restored.Compute(registry), Is.Not.EqualTo(before));
            Assert.That(hashQueries, Is.Zero);
            Assert.That(liveReads, Is.EqualTo(6));
        }

        [Test]
        public void SessionFromChangedArtifactVersionIsDiscarded()
        {
            StyleRecipeRegistry registry = CreateRegistry(1, out _);
            uint dependencyVersion = 1;
            int hashQueries = 0;
            var fingerprint = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => dependencyVersion,
                path =>
                {
                    hashQueries++;
                    return "saved-hash:" + dependencyVersion + ":" + path;
                },
                target => EditorJsonUtility.ToJson(target));
            string before = fingerprint.Compute(registry);
            string savedSession = fingerprint.ExportSessionState();
            dependencyVersion++;

            fingerprint.ImportSessionState(savedSession);

            Assert.That(fingerprint.ExportSessionState(), Is.Empty);
            Assert.That(fingerprint.Compute(registry), Is.Not.EqualTo(before));
            Assert.That(hashQueries, Is.EqualTo(8));
            dependencyVersion++;
            Assert.That(fingerprint.ExportSessionState(), Is.Empty,
                "A version change immediately before assembly reload must discard old hashes.");
        }

        [TestCase("not-json")]
        [TestCase("{}")]
        [TestCase("{\"FormatVersion\":1,\"DependencyVersion\":1,\"Paths\":[\"Assets/a\"],\"Hashes\":[]}")]
        public void InvalidSessionStateFallsBackToFreshDirectHashes(string savedSession)
        {
            StyleRecipeRegistry registry = CreateRegistry(1, out _);
            int hashQueries = 0;
            var fingerprint = new StyleRegistryDependencyFingerprint(
                target => _paths[target],
                () => 1,
                path =>
                {
                    hashQueries++;
                    return "saved-hash:" + path;
                },
                target => EditorJsonUtility.ToJson(target));

            fingerprint.ImportSessionState(savedSession);

            Assert.That(fingerprint.Compute(registry), Is.Not.Empty);
            Assert.That(hashQueries, Is.EqualTo(4));
        }

        private StyleRecipeRegistry CreateRegistry(int recipeCount, out ColorToken sharedToken)
        {
            sharedToken = CreateAsset<ColorToken>("SharedToken");
            var recipes = new PrefabStyleRecipe[recipeCount];
            for (int index = 0; index < recipeCount; index++)
            {
                PrefabStyleRecipe recipe = CreateAsset<PrefabStyleRecipe>("Recipe" + index);
                var owner = new GameObject("Owner" + index);
                _objects.Add(owner);
                _paths.Add(owner, "Assets/Owner" + index + ".prefab");
                var binding = new PrefabStyleRecipe.GraphicColorBinding();
                SetPrivateField(binding, "_color", sharedToken);
                SetPrivateField(recipe, "_ownerPrefab", owner);
                SetPrivateField(recipe, "_graphicColors", new[] { binding });
                recipes[index] = recipe;
            }

            StyleRecipeRegistry registry = CreateAsset<StyleRecipeRegistry>("Registry");
            SetPrivateField(registry, "_recipes", recipes);
            return registry;
        }

        private T CreateAsset<T>(string name) where T : ScriptableObject
        {
            T target = ScriptableObject.CreateInstance<T>();
            target.name = name;
            _objects.Add(target);
            _paths.Add(target, "Assets/" + name + ".asset");
            return target;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
