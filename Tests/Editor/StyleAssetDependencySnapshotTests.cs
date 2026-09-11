using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleAssetDependencySnapshotTests
    {
        [Test]
        public void MissingImplicitDependencyFallsBackToNativeResults()
        {
            var fixture = new DependencyFixture();
            fixture.Native["Assets/First.prefab"] = new[]
            {
                "Assets/First.prefab", "Assets/Implicit.prefab", "Assets/EditorIcon.png",
            };
            fixture.Native["Assets/Second.prefab"] = new[] { "Assets/Second.prefab", "Assets/EditorIcon.png" };
            StyleAssetDependencySnapshot snapshot = fixture.CreateSnapshot();

            HashSet<string> dependents = snapshot.FindDependents(
                new[] { "Assets/First.prefab", "Assets/Second.prefab" },
                Targets("Assets/Implicit.prefab"));

            Assert.That(dependents, Is.EquivalentTo(new[] { "Assets/First.prefab" }));
            Assert.That(fixture.BatchQueries.Count, Is.EqualTo(1));
            Assert.That(fixture.NativeReads.Keys, Is.EquivalentTo(fixture.Native.Keys));
            Assert.That(snapshot.GetDependencies("Assets/First.prefab"),
                Is.EqualTo(fixture.Native["Assets/First.prefab"]));
            Assert.That(fixture.NativeReads.Values, Is.All.EqualTo(1));
        }

        [Test]
        public void SharedGraphBoundsNativeReadsAndSharesEveryAssetType()
        {
            var fixture = new DependencyFixture();
            fixture.Direct["Assets/First.prefab"] = new[] { "Assets/Shared.asset" };
            fixture.Direct["Assets/Second.prefab"] = new[] { "assets/shared.asset" };
            fixture.Direct["Assets/Shared.asset"] = new[] { "Assets/Target.prefab", "Assets/Shared.mat" };
            fixture.Direct["Assets/Shared.mat"] = new[] { "Assets/Texture.png" };
            fixture.Native["Assets/First.prefab"] = new[] { "Assets/First.prefab", "Assets/Target.prefab" };
            fixture.Native["Assets/Second.prefab"] = new[] { "Assets/Second.prefab", "Assets/Target.prefab" };
            fixture.Native["Assets/Unrelated.prefab"] = new[] { "Assets/Unrelated.prefab" };
            StyleAssetDependencySnapshot snapshot = fixture.CreateSnapshot();

            HashSet<string> dependents = snapshot.FindDependents(
                new[] { "Assets/First.prefab", "Assets/Second.prefab", "Assets/Unrelated.prefab" },
                Targets("Assets/Target.prefab"));
            snapshot.FindDependents(new[] { "assets/first.prefab" }, Targets("assets/target.prefab"));
            snapshot.GetDependencies("Assets/First.prefab");

            Assert.That(dependents, Is.EquivalentTo(new[] { "Assets/First.prefab", "Assets/Second.prefab" }));
            Assert.That(fixture.DirectReads.Count, Is.EqualTo(7));
            Assert.That(fixture.DirectReads.Values, Is.All.EqualTo(1));
            Assert.That(fixture.NativeReads.Keys,
                Is.EquivalentTo(new[] { "Assets/First.prefab", "Assets/Second.prefab" }));
            Assert.That(fixture.NativeReads.Values, Is.All.EqualTo(1));
            Assert.That(fixture.BatchQueries.Count, Is.EqualTo(1));
            Assert.That(fixture.BatchQueries[0], Is.EqualTo(new[] { "Assets/Unrelated.prefab" }));
        }

        [Test]
        public void NativeResultsRejectAFalsePositiveDirectHint()
        {
            var fixture = new DependencyFixture();
            fixture.Direct["Assets/Owner.prefab"] = new[] { "Assets/Target.prefab" };
            fixture.Native["Assets/Owner.prefab"] = new[] { "Assets/Owner.prefab" };
            StyleAssetDependencySnapshot snapshot = fixture.CreateSnapshot();

            HashSet<string> dependents = snapshot.FindDependents(
                new[] { "Assets/Owner.prefab" }, Targets("Assets/Target.prefab"));

            Assert.That(dependents, Is.Empty);
            Assert.That(fixture.NativeReads["Assets/Owner.prefab"], Is.EqualTo(1));
            Assert.That(fixture.BatchQueries, Is.Empty);
            Assert.That(snapshot.GetDependencies("Assets/Owner.prefab"),
                Is.EqualTo(new[] { "Assets/Owner.prefab" }));
        }

        [Test]
        public void CyclesPreserveDifferentRootsAndIncludeTheTargetItself()
        {
            var fixture = new DependencyFixture();
            fixture.Direct["Assets/A.prefab"] = new[] { "Assets/B.asset" };
            fixture.Direct["Assets/B.asset"] = new[] { "Assets/A.prefab", "Assets/Target.prefab" };
            string[] closure = { "Assets/A.prefab", "Assets/B.asset", "Assets/Target.prefab", "Assets/EditorIcon.png" };
            fixture.Native["Assets/A.prefab"] = closure;
            fixture.Native["Assets/B.asset"] = closure;
            StyleAssetDependencySnapshot snapshot = fixture.CreateSnapshot();

            HashSet<string> dependents = snapshot.FindDependents(
                new[] { "Assets/A.prefab", "Assets/B.asset", "Assets/Target.prefab" },
                Targets("Assets/Target.prefab"));

            Assert.That(dependents,
                Is.EquivalentTo(new[] { "Assets/A.prefab", "Assets/B.asset", "Assets/Target.prefab" }));
            Assert.That(snapshot.GetDependencies("Assets/B.asset"), Is.EqualTo(closure));
            Assert.That(fixture.DirectReads.Count, Is.EqualTo(3));
            Assert.That(fixture.DirectReads.Values, Is.All.EqualTo(1));
            Assert.That(fixture.NativeReads.Count, Is.EqualTo(2));
            Assert.That(fixture.NativeReads.Values, Is.All.EqualTo(1));
            Assert.That(fixture.BatchQueries, Is.Empty);
        }

        [Test]
        public void SeparateSnapshotsReadChangedDirectAndNativeEdgesAgain()
        {
            var fixture = new DependencyFixture();
            fixture.Direct["Assets/Owner.prefab"] = new[] { "Assets/Before.prefab" };
            fixture.Native["Assets/Owner.prefab"] = new[] { "Assets/Owner.prefab", "Assets/Before.prefab" };
            StyleAssetDependencySnapshot first = fixture.CreateSnapshot();
            Assert.That(first.FindDependents(new[] { "Assets/Owner.prefab" }, Targets("Assets/After.prefab")), Is.Empty);
            string[] before = first.GetDependencies("Assets/Owner.prefab");

            fixture.Direct["Assets/Owner.prefab"] = new[] { "Assets/After.prefab" };
            fixture.Native["Assets/Owner.prefab"] = new[] { "Assets/Owner.prefab", "Assets/After.prefab" };
            StyleAssetDependencySnapshot second = fixture.CreateSnapshot();

            Assert.That(second.FindDependents(new[] { "Assets/Owner.prefab" }, Targets("Assets/After.prefab")),
                Is.EquivalentTo(new[] { "Assets/Owner.prefab" }));
            Assert.That(first.GetDependencies("Assets/Owner.prefab"), Is.SameAs(before));
            Assert.That(before, Is.EqualTo(new[] { "Assets/Owner.prefab", "Assets/Before.prefab" }));
            Assert.That(second.GetDependencies("Assets/Owner.prefab"), Is.EqualTo(fixture.Native["Assets/Owner.prefab"]));
            Assert.That(fixture.DirectReads["Assets/Owner.prefab"], Is.EqualTo(2));
            Assert.That(fixture.NativeReads["Assets/Owner.prefab"], Is.EqualTo(2));
        }

        private static HashSet<string> Targets(params string[] paths)
        {
            return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        }

        private sealed class DependencyFixture
        {
            public Dictionary<string, string[]> Direct { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string[]> Native { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> DirectReads { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> NativeReads { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string[]> BatchQueries { get; } = new();

            public StyleAssetDependencySnapshot CreateSnapshot()
            {
                return new StyleAssetDependencySnapshot(
                    path =>
                    {
                        RecordRead(DirectReads, path);
                        return Direct.TryGetValue(path, out string[] dependencies) ? dependencies : Array.Empty<string>();
                    },
                    path =>
                    {
                        RecordRead(NativeReads, path);
                        return Native[path];
                    },
                    paths =>
                    {
                        BatchQueries.Add(paths);
                        return paths.SelectMany(path => Native[path]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    });
            }

            private static void RecordRead(IDictionary<string, int> reads, string path)
            {
                reads.TryGetValue(path, out int count);
                reads[path] = count + 1;
            }
        }
    }
}
