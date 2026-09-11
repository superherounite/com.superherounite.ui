using System;
using System.IO;

using NUnit.Framework;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class PrefabTargetResolverTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = "Assets/__SuperHeroUIResolverTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_testRoot))
            {
                AssetDatabase.DeleteAsset(_testRoot);
            }

            _testRoot = null;
        }

        [Test]
        public void MultipleBindingsReuseOneIndexAndResolveInactiveTargets()
        {
            string ownerPath = CreateOwner(32);
            GameObject root = PrefabUtility.LoadPrefabContents(ownerPath);
            try
            {
                var targetIndex = new PrefabTargetResolver.TargetIndex(root);
                Image[] images = root.GetComponentsInChildren<Image>(true);
                Assert.That(images.Length, Is.EqualTo(33));
                Assert.That(targetIndex.BuildCount, Is.Zero);

                foreach (Image image in images)
                {
                    PrefabTargetReference target = Capture(root, image);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        Assert.That(
                            PrefabTargetResolver.TryResolveTarget(
                                targetIndex,
                                target,
                                typeof(Image),
                                out Component resolved,
                                out string error),
                            Is.True,
                            error);
                        Assert.That(resolved, Is.SameAs(image));
                    }
                }

                Assert.That(targetIndex.BuildCount, Is.EqualTo(1));
                Assert.That(
                    targetIndex.IndexedComponentCount,
                    Is.EqualTo(root.GetComponentsInChildren<Component>(true).Length));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void InvalidCapturedTargetDoesNotBuildIndex()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CreateOwner(0));
            try
            {
                var targetIndex = new PrefabTargetResolver.TargetIndex(root);
                var target = new PrefabTargetReference();
                target.Configure("invalid", root.name, typeof(Image).FullName, PrefabTargetKind.Image);

                Assert.That(
                    PrefabTargetResolver.TryResolveTarget(
                        targetIndex,
                        target,
                        typeof(Image),
                        out Component resolved,
                        out string error),
                    Is.False);
                Assert.That(resolved, Is.Null);
                StringAssert.Contains("GlobalObjectId is invalid", error);
                Assert.That(targetIndex.BuildCount, Is.Zero);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void IndexedLookupPreservesFullIdentityAndTypeValidation()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CreateOwner(0));
            try
            {
                var targetIndex = new PrefabTargetResolver.TargetIndex(root);
                PrefabTargetReference target = Capture(root, root.GetComponent<Image>());

                Assert.That(
                    PrefabTargetResolver.TryResolveTarget(
                        targetIndex,
                        target,
                        typeof(Button),
                        out _,
                        out string error),
                    Is.False);
                StringAssert.Contains("The target type changed", error);

                var changedComponent = new PrefabTargetReference();
                changedComponent.Configure(
                    target.GlobalObjectId,
                    target.DisplayPath,
                    typeof(Button).FullName,
                    PrefabTargetKind.Image);
                Assert.That(
                    PrefabTargetResolver.TryResolveTarget(
                        targetIndex,
                        changedComponent,
                        typeof(Image),
                        out _,
                        out error),
                    Is.False);
                StringAssert.Contains("The captured component changed", error);

                string[] identityParts = target.GlobalObjectId.Split('-');
                identityParts[4] = "18446744073709551615";
                var missingTarget = new PrefabTargetReference();
                missingTarget.Configure(
                    string.Join("-", identityParts),
                    target.DisplayPath,
                    target.ComponentType,
                    target.ExpectedKind);
                Assert.That(GlobalObjectId.TryParse(missingTarget.GlobalObjectId, out _), Is.True);
                Assert.That(
                    PrefabTargetResolver.TryResolveTarget(
                        targetIndex,
                        missingTarget,
                        typeof(Image),
                        out _,
                        out error),
                    Is.False);
                StringAssert.Contains("The target no longer exists", error);
                Assert.That(targetIndex.BuildCount, Is.EqualTo(1));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void RepeatedNestedPrefabInstancesKeepDistinctTargetIdentities()
        {
            string sourcePath = CreateOwner(0);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            string ownerPath = _testRoot + "/Nested Owner.prefab";
            var owner = new GameObject("Nested Owner", typeof(RectTransform));
            try
            {
                PrefabUtility.InstantiatePrefab(source, owner.transform);
                PrefabUtility.InstantiatePrefab(source, owner.transform);
                PrefabUtility.SaveAsPrefabAsset(owner, ownerPath);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ownerPath);
            try
            {
                var targetIndex = new PrefabTargetResolver.TargetIndex(root);
                Image[] images = root.GetComponentsInChildren<Image>(true);
                Assert.That(images.Length, Is.EqualTo(2));
                PrefabTargetReference first = Capture(root, images[0]);
                PrefabTargetReference second = Capture(root, images[1]);
                Assert.That(first.GlobalObjectId, Is.Not.EqualTo(second.GlobalObjectId));

                for (int index = 0; index < images.Length; index++)
                {
                    Assert.That(
                        PrefabTargetResolver.TryResolveTarget(
                            targetIndex,
                            Capture(root, images[index]),
                            typeof(Image),
                            out Component resolved,
                            out string error),
                        Is.True,
                        error);
                    Assert.That(resolved, Is.SameAs(images[index]));
                }

                Assert.That(targetIndex.BuildCount, Is.EqualTo(1));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private string CreateOwner(int childCount)
        {
            string path = _testRoot + "/Owner.prefab";
            var root = new GameObject("Owner", typeof(RectTransform), typeof(Image));
            try
            {
                for (int index = 0; index < childCount; index++)
                {
                    var child = new GameObject("Image " + index, typeof(RectTransform), typeof(Image));
                    child.transform.SetParent(root.transform, false);
                    child.SetActive(index % 2 == 0);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static PrefabTargetReference Capture(GameObject root, Component component)
        {
            var target = new PrefabTargetReference();
            target.Configure(
                GlobalObjectId.GetGlobalObjectIdSlow(component).ToString(),
                PrefabTargetResolver.GetDisplayPath(root.transform, component.transform),
                component.GetType().FullName,
                PrefabTargetKind.Image);
            return target;
        }
    }
}
