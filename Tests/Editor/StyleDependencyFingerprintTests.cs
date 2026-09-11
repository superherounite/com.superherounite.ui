using System;
using System.Collections.Generic;
using System.Text;

using NUnit.Framework;

using TMPro;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleDependencyFingerprintTests
    {
        private readonly List<Object> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object target in _objects)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            _objects.Clear();
        }

        [Test]
        public void RepeatedReferencesPreserveOrderAndReadSharedValuesOnce()
        {
            ColorToken token = CreateToken("Shared");
            int hashReads = 0;
            int jsonReads = 0;
            var fingerprint = new StyleDependencyFingerprint(
                target => "Assets/Shared.asset",
                path =>
                {
                    hashReads++;
                    return "dependency-hash";
                },
                target =>
                {
                    jsonReads++;
                    return "token-json";
                });
            var text = new StringBuilder();

            fingerprint.AppendObject(text, token);
            string firstEntry = text.ToString();
            fingerprint.AppendObject(text, token);

            Assert.That(text.ToString(), Is.EqualTo(firstEntry + firstEntry));
            StringAssert.StartsWith("Assets/Shared.asset" + Environment.NewLine + "dependency-hash" + Environment.NewLine, firstEntry);
            Assert.That(fingerprint.GetSerializedDigest(token), Is.Not.Empty);
            Assert.That(fingerprint.GetSerializedDigest(token), Is.Not.EqualTo("token-json"));
            Assert.That(hashReads, Is.EqualTo(1));
            Assert.That(jsonReads, Is.EqualTo(1));
        }

        [Test]
        public void ObjectsSharingAnAssetPathRetainSeparateSerializedDigests()
        {
            ColorToken first = CreateToken("First");
            ColorToken second = CreateToken("Second");
            int hashReads = 0;
            int jsonReads = 0;
            var fingerprint = new StyleDependencyFingerprint(
                target => target == first ? "Assets/Shared.asset" : "assets/shared.asset",
                path =>
                {
                    hashReads++;
                    return "dependency-hash";
                },
                target =>
                {
                    jsonReads++;
                    return target.name;
                });
            var firstText = new StringBuilder();
            var secondText = new StringBuilder();
            var repeatedText = new StringBuilder();

            fingerprint.AppendObject(firstText, first);
            fingerprint.AppendObject(secondText, second);
            fingerprint.AppendObject(repeatedText, first);

            Assert.That(repeatedText.ToString(), Is.EqualTo(firstText.ToString()));
            StringAssert.StartsWith("Assets/Shared.asset" + Environment.NewLine, firstText.ToString());
            StringAssert.StartsWith("assets/shared.asset" + Environment.NewLine, secondText.ToString());
            Assert.That(fingerprint.GetSerializedDigest(first), Is.Not.EqualTo(fingerprint.GetSerializedDigest(second)));
            Assert.That(hashReads, Is.EqualTo(1));
            Assert.That(jsonReads, Is.EqualTo(2));
        }

        [Test]
        public void UnsavedScriptableObjectsIncludeSerializedDigestWithoutDependencyHash()
        {
            ColorToken token = CreateToken("Unsaved");
            int hashReads = 0;
            int jsonReads = 0;
            var fingerprint = new StyleDependencyFingerprint(
                target => string.Empty,
                path =>
                {
                    hashReads++;
                    return "unexpected-hash";
                },
                target =>
                {
                    jsonReads++;
                    return "unsaved-json";
                });
            var text = new StringBuilder();

            fingerprint.AppendObject(text, token);
            string firstEntry = text.ToString();
            fingerprint.AppendObject(text, token);

            Assert.That(text.ToString(), Is.EqualTo(firstEntry + firstEntry));
            StringAssert.StartsWith(Environment.NewLine, firstEntry);
            Assert.That(fingerprint.GetSerializedDigest(token), Is.Not.Empty);
            Assert.That(fingerprint.GetSerializedDigest(token), Is.Not.EqualTo("unsaved-json"));
            Assert.That(hashReads, Is.Zero);
            Assert.That(jsonReads, Is.EqualTo(1));
        }

        [Test]
        public void LargeSharedJsonIsSerializedOnceAndProducesBoundedRepeatedOutput()
        {
            ColorToken token = CreateToken("Shared Large Asset");
            string serializedJson = "{\"glyphs\":\"" + new string('x', 1024 * 1024) + "\"}";
            int hashReads = 0;
            int jsonReads = 0;
            var fingerprint = new StyleDependencyFingerprint(
                target => "Assets/Shared.asset",
                path =>
                {
                    hashReads++;
                    return "dependency-hash";
                },
                target =>
                {
                    jsonReads++;
                    return serializedJson;
                });
            var text = new StringBuilder();
            const int ReferenceCount = 100;

            string digest = fingerprint.GetSerializedDigest(token);
            fingerprint.AppendObject(text, token);
            string firstEntry = text.ToString();
            for (int index = 1; index < ReferenceCount; index++)
            {
                fingerprint.AppendObject(text, token);
            }

            Assert.That(fingerprint.GetSerializedDigest(token), Is.EqualTo(digest));
            Assert.That(firstEntry.Length, Is.LessThan(1024));
            Assert.That(text.Length, Is.EqualTo(firstEntry.Length * ReferenceCount));
            Assert.That(text.Length, Is.LessThan(serializedJson.Length));
            StringAssert.DoesNotContain(serializedJson, text.ToString());
            Assert.That(hashReads, Is.EqualTo(1));
            Assert.That(jsonReads, Is.EqualTo(1));
        }

        [TestCase("한글😀", "한글😁")]
        [TestCase("\\uD55C\\uAE00\\uD83D\\uDE00", "\\uD55C\\uAE00\\uD83D\\uDE01")]
        [TestCase("tail-a", "tail-b")]
        public void SeparateOperationsIncludeUnicodeAndLargeJsonTailChanges(string originalTail, string changedTail)
        {
            ColorToken token = CreateToken("Large Unicode Asset");
            string jsonPrefix = "{\"glyphs\":\"" + new string('x', 1024 * 1024) + "\",\"name\":\"";
            string serializedJson = jsonPrefix + originalTail + "\"}";

            string ReadDigest()
            {
                var fingerprint = new StyleDependencyFingerprint(
                    target => "Assets/Large.asset",
                    path => "dependency-hash",
                    target => serializedJson);
                return fingerprint.GetSerializedDigest(token);
            }

            string before = ReadDigest();
            Assert.That(ReadDigest(), Is.EqualTo(before));

            serializedJson = jsonPrefix + changedTail + "\"}";
            Assert.That(ReadDigest(), Is.Not.EqualTo(before));

            serializedJson = jsonPrefix + originalTail + "\"}";
            Assert.That(ReadDigest(), Is.EqualTo(before));
        }

        [Test]
        public void MissingObjectsAppendOnlyTheMissingMarker()
        {
            ColorToken token = CreateToken("Destroyed");
            Object.DestroyImmediate(token);
            int providerCalls = 0;
            var fingerprint = new StyleDependencyFingerprint(
                target =>
                {
                    providerCalls++;
                    return string.Empty;
                },
                path =>
                {
                    providerCalls++;
                    return string.Empty;
                },
                target =>
                {
                    providerCalls++;
                    return string.Empty;
                });
            var text = new StringBuilder();

            fingerprint.AppendObject(text, null);
            fingerprint.AppendObject(text, token);

            Assert.That(text.ToString(), Is.EqualTo(
                "<missing>" + Environment.NewLine + "<missing>" + Environment.NewLine));
            Assert.That(providerCalls, Is.Zero);
        }

        [Test]
        public void NonScriptableObjectsDoNotIncludeScriptableObjectJson()
        {
            var prefab = new GameObject("Prefab");
            _objects.Add(prefab);
            int jsonReads = 0;
            var fingerprint = new StyleDependencyFingerprint(
                target => "Assets/Owner.prefab",
                path => "prefab-hash",
                target =>
                {
                    jsonReads++;
                    return "unexpected-json";
                });
            var text = new StringBuilder();

            fingerprint.AppendObject(text, prefab);

            Assert.That(text.ToString(), Is.EqualTo(
                "Assets/Owner.prefab" + Environment.NewLine + "prefab-hash" + Environment.NewLine));
            Assert.That(jsonReads, Is.Zero);
        }

        [Test]
        public void SeparateOperationsReadLatestUnsavedScriptableObjectState()
        {
            ColorToken token = CreateToken("Before");
            var before = new StringBuilder();
            new StyleDependencyFingerprint().AppendObject(before, token);
            var unchanged = new StringBuilder();
            new StyleDependencyFingerprint().AppendObject(unchanged, token);
            Assert.That(unchanged.ToString(), Is.EqualTo(before.ToString()));

            token.name = "After";
            EditorUtility.SetDirty(token);
            var after = new StringBuilder();
            new StyleDependencyFingerprint().AppendObject(after, token);

            Assert.That(after.ToString(), Is.Not.EqualTo(before.ToString()));

            Assert.That(EditorUtility.IsDirty(token), Is.True);
            token.name = "Changed While Already Dirty";
            var changedAgain = new StringBuilder();
            new StyleDependencyFingerprint().AppendObject(changedAgain, token);

            Assert.That(changedAgain.ToString(), Is.Not.EqualTo(after.ToString()));
        }

        [Test]
        public void PersistentFontFallbackEditsInvalidateFingerprintWithoutDirtyOrSavedHashChanges()
        {
            string folderName = "SuperHeroUIFingerprintTests_" + Guid.NewGuid().ToString("N");
            string testRoot = "Assets/" + folderName;
            string fontPath = testRoot + "/Font.asset";
            TMP_FontAsset font = null;
            TMP_FontAsset fallback = null;

            try
            {
                AssetDatabase.CreateFolder("Assets", folderName);
                font = ScriptableObject.CreateInstance<TMP_FontAsset>();
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                AssetDatabase.CreateAsset(font, fontPath);
                fallback = ScriptableObject.CreateInstance<TMP_FontAsset>();
                AssetDatabase.CreateAsset(fallback, testRoot + "/Fallback.asset");
                AssetDatabase.SaveAssetIfDirty(font);
                AssetDatabase.SaveAssetIfDirty(fallback);
                Assert.That(EditorUtility.IsPersistent(font), Is.True);
                Assert.That(EditorUtility.IsPersistent(fallback), Is.True);

                var before = new StringBuilder();
                new StyleDependencyFingerprint().AppendObject(before, font);
                Hash128 savedAssetDependencyHash = AssetDatabase.GetAssetDependencyHash(fontPath);
                int dirtyCount = EditorUtility.GetDirtyCount(font);
                Assert.That(EditorUtility.IsDirty(font), Is.False);

                // Direct managed-list edits do not notify Unity's dirty tracking.
                font.fallbackFontAssetTable.Add(fallback);
                var after = new StringBuilder();
                new StyleDependencyFingerprint().AppendObject(after, font);

                Assert.That(EditorUtility.GetDirtyCount(font), Is.EqualTo(dirtyCount));
                Assert.That(EditorUtility.IsDirty(font), Is.False);
                Assert.That(AssetDatabase.GetAssetDependencyHash(fontPath), Is.EqualTo(savedAssetDependencyHash));
                Assert.That(after.ToString(), Is.Not.EqualTo(before.ToString()));

                font.fallbackFontAssetTable.RemoveAt(0);
                var restored = new StringBuilder();
                new StyleDependencyFingerprint().AppendObject(restored, font);

                Assert.That(EditorUtility.GetDirtyCount(font), Is.EqualTo(dirtyCount));
                Assert.That(EditorUtility.IsDirty(font), Is.False);
                Assert.That(AssetDatabase.GetAssetDependencyHash(fontPath), Is.EqualTo(savedAssetDependencyHash));
                Assert.That(restored.ToString(), Is.EqualTo(before.ToString()));
            }
            finally
            {
                AssetDatabase.DeleteAsset(testRoot);
                if (font != null && !EditorUtility.IsPersistent(font))
                {
                    Object.DestroyImmediate(font);
                }

                if (fallback != null && !EditorUtility.IsPersistent(fallback))
                {
                    Object.DestroyImmediate(fallback);
                }
            }
        }

        private ColorToken CreateToken(string name)
        {
            var token = ScriptableObject.CreateInstance<ColorToken>();
            token.name = name;
            _objects.Add(token);
            return token;
        }
    }
}
