using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using TMPro;

using UnityEditor;

using UnityEngine;

using Object = UnityEngine.Object;

namespace SuperHeroUnite.UI.Editor.Tests
{
    public sealed class StyleConsumerRecipeLookupTests
    {
        private const string CapturedId = "GlobalObjectId_V1-1-0123456789abcdef0123456789abcdef-12345-0";
        private readonly List<Object> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object target in _objects)
            {
                Object.DestroyImmediate(target);
            }

            _objects.Clear();
        }

        [Test]
        public void RepeatedTextPropertyComparisonsResolveCapturedTargetOncePerConsumer()
        {
            int sourceResolutions = 0;
            var lookup = new StyleConsumerRecipeLookup(Array.Empty<PrefabStyleRecipe>(), targetId =>
            {
                sourceResolutions++;
                return new[] { "source-guid:100", "base-guid:50" };
            });
            PrefabTargetReference target = CreateTarget("Variant/Text");
            PrefabTargetReference anotherBinding = CreateTarget("Variant/Text");
            string[] sourceKeys = { "base-guid:50" };
            const int TextPropertyCount = 13;
            const int InstanceCount = 20;

            for (int instance = 0; instance < InstanceCount; instance++)
            {
                for (int property = 0; property < TextPropertyCount; property++)
                {
                    Assert.That(lookup.TargetsMatch(
                        instance % 2 == 0 ? target : anotherBinding,
                        sourceKeys,
                        "Base/Other Display Name",
                        typeof(TextMeshProUGUI).FullName), Is.True);
                }
            }

            Assert.That(sourceResolutions, Is.EqualTo(1));
            TestContext.WriteLine(
                $"Text specialization target comparisons: {InstanceCount * TextPropertyCount}; "
                + $"native target/source-chain resolutions: {sourceResolutions}.");
        }

        [Test]
        public void RelativePathFallbackPreservesTypeAndPathChecks()
        {
            int sourceResolutions = 0;
            var lookup = new StyleConsumerRecipeLookup(Array.Empty<PrefabStyleRecipe>(), targetId =>
            {
                sourceResolutions++;
                return Array.Empty<string>();
            });
            PrefabTargetReference target = CreateTarget("Variant/Container/Text");

            Assert.That(lookup.TargetsMatch(target, Array.Empty<string>(), "Base/Container/Text", typeof(TextMeshProUGUI).FullName), Is.True);
            Assert.That(lookup.TargetsMatch(target, Array.Empty<string>(), "Base/Other/Text", typeof(TextMeshProUGUI).FullName), Is.False);
            Assert.That(lookup.TargetsMatch(target, Array.Empty<string>(), "Base/Container/Text", "Other.Component"), Is.False);
            Assert.That(sourceResolutions, Is.EqualTo(1));
            target.Configure("invalid-id", "Variant/Container/Text", typeof(TextMeshProUGUI).FullName, PrefabTargetKind.Text);
            Assert.That(lookup.TargetsMatch(target, Array.Empty<string>(), "Base/Container/Text", typeof(TextMeshProUGUI).FullName), Is.False);
            Assert.That(sourceResolutions, Is.EqualTo(1));
        }

        [Test]
        public void NextConsumerReadsFreshSourceIdentityAndBaseRecipeRelationships()
        {
            PrefabStyleRecipe baseRecipe = CreateRecipe();
            PrefabStyleRecipe variant = CreateRecipe();
            SetPrivateField(variant, "_baseRecipe", baseRecipe);
            string sourceKey = "old-source";
            int sourceResolutions = 0;
            StyleConsumerRecipeLookup CreateLookup()
            {
                return new StyleConsumerRecipeLookup(new[] { baseRecipe, variant }, target => string.Empty, targetId =>
                {
                    sourceResolutions++;
                    return new[] { sourceKey };
                });
            }

            PrefabTargetReference target = CreateTarget("Variant/Text");
            StyleConsumerRecipeLookup first = CreateLookup();
            Assert.That(first.TargetsMatch(target, new[] { "old-source" }, "Different/Child", string.Empty), Is.True);
            Assert.That(first.IsSpecializationOf(variant, baseRecipe), Is.True);
            sourceKey = "new-source";
            SetPrivateField(variant, "_baseRecipe", null);

            StyleConsumerRecipeLookup next = CreateLookup();

            Assert.That(next.TargetsMatch(target, new[] { "old-source" }, "Different/Child", string.Empty), Is.False);
            Assert.That(next.TargetsMatch(target, new[] { "new-source" }, "Different/Child", string.Empty), Is.True);
            Assert.That(next.IsSpecializationOf(variant, baseRecipe), Is.False);
            Assert.That(sourceResolutions, Is.EqualTo(2));
        }

        [Test]
        public void OwnerIndexPreservesRegistrationOrderWithoutRepeatingAssetPathQueries()
        {
            PrefabStyleRecipe first = CreateRecipe();
            PrefabStyleRecipe second = CreateRecipe();
            var owner = new GameObject("Owner");
            var consumer = new GameObject("Consumer");
            _objects.Add(owner);
            _objects.Add(consumer);
            SetPrivateField(first, "_ownerPrefab", owner);
            SetPrivateField(second, "_ownerPrefab", owner);
            SetPrivateField(second, "_consumerPrefabs", new[] { consumer });
            int assetPathQueries = 0;
            var lookup = new StyleConsumerRecipeLookup(new[] { first, second }, target =>
            {
                assetPathQueries++;
                return target == owner ? "Assets/Owner.prefab" : "Assets/Consumer.prefab";
            }, targetId => Array.Empty<string>());

            for (int index = 0; index < 100; index++)
            {
                Assert.That(lookup.GetOwners("assets/owner.prefab"), Is.EqualTo(new[] { first, second }));
                Assert.That(lookup.IsRegisteredPrefab("assets/consumer.prefab"), Is.True);
                Assert.That(lookup.GetOwners("Assets/Unregistered.prefab"), Is.Empty);
            }

            Assert.That(assetPathQueries, Is.EqualTo(3));
        }

        [Test]
        public void TransitiveSpecializationsAndCyclesTerminateWithSameAncestorMembership()
        {
            PrefabStyleRecipe root = CreateRecipe();
            PrefabStyleRecipe middle = CreateRecipe();
            PrefabStyleRecipe leaf = CreateRecipe();
            PrefabStyleRecipe unrelated = CreateRecipe();
            SetPrivateField(middle, "_baseRecipe", root);
            SetPrivateField(leaf, "_baseRecipe", middle);
            SetPrivateField(root, "_baseRecipe", leaf);
            var lookup = new StyleConsumerRecipeLookup(new[] { root, middle, leaf, unrelated },
                target => string.Empty, targetId => Array.Empty<string>());

            Assert.That(lookup.IsSpecializationOf(leaf, root), Is.True);
            Assert.That(lookup.IsSpecializationOf(leaf, middle), Is.True);
            Assert.That(lookup.IsSpecializationOf(leaf, unrelated), Is.False);
            Assert.That(lookup.IsSpecializationOf(leaf, leaf), Is.False);
        }

        [Test]
        public void MissingBaseRecipesUseNearestRegisteredSourceWithoutWritingRecipes()
        {
            PrefabStyleRecipe root = CreateRecipeWithOwner("Root");
            PrefabStyleRecipe middle = CreateRecipeWithOwner("Middle");
            PrefabStyleRecipe leaf = CreateRecipeWithOwner("Leaf");
            PrefabStyleRecipe unrelated = CreateRecipeWithOwner("Unrelated");
            var unregistered = new GameObject("Unregistered intermediate");
            _objects.Add(unregistered);
            var sources = new Dictionary<GameObject, GameObject>
            {
                [leaf.OwnerPrefab] = unregistered,
                [unregistered] = middle.OwnerPrefab,
                [middle.OwnerPrefab] = root.OwnerPrefab
            };
            var sourceQueries = new Dictionary<GameObject, int>();
            string middleBefore = EditorJsonUtility.ToJson(middle);
            string leafBefore = EditorJsonUtility.ToJson(leaf);
            var relationships = new StyleRecipeRelationships(new[] { leaf, null, root, middle, unrelated }, owner =>
            {
                sourceQueries.TryGetValue(owner, out int count);
                sourceQueries[owner] = count + 1;
                return sources.TryGetValue(owner, out GameObject source) ? source : null;
            });

            Assert.That(relationships.GetBaseRecipe(leaf), Is.SameAs(middle));
            Assert.That(relationships.GetBaseRecipe(middle), Is.SameAs(root));
            for (int index = 0; index < 100; index++)
            {
                Assert.That(relationships.IsSpecializationOf(leaf, root), Is.True);
                Assert.That(relationships.IsSpecializationOf(leaf, middle), Is.True);
            }

            Assert.That(sourceQueries.Values, Is.All.EqualTo(1));
            Assert.That(sourceQueries.ContainsKey(unrelated.OwnerPrefab), Is.False);
            Assert.That(leaf.BaseRecipe, Is.Null);
            Assert.That(middle.BaseRecipe, Is.Null);
            Assert.That(EditorJsonUtility.ToJson(middle), Is.EqualTo(middleBefore));
            Assert.That(EditorJsonUtility.ToJson(leaf), Is.EqualTo(leafBefore));
        }

        [Test]
        public void NextConsumerUsesChangedPrefabSourceAncestry()
        {
            PrefabStyleRecipe firstBase = CreateRecipeWithOwner("First base");
            PrefabStyleRecipe nextBase = CreateRecipeWithOwner("Next base");
            PrefabStyleRecipe variant = CreateRecipeWithOwner("Variant");
            PrefabStyleRecipe[] recipes = { firstBase, nextBase, variant };
            GameObject currentSource = firstBase.OwnerPrefab;
            StyleConsumerRecipeLookup CreateLookup()
            {
                var relationships = new StyleRecipeRelationships(recipes,
                    owner => owner == variant.OwnerPrefab ? currentSource : null);
                return new StyleConsumerRecipeLookup(recipes, owner => owner.name,
                    target => Array.Empty<string>(), relationships);
            }

            StyleConsumerRecipeLookup first = CreateLookup();
            Assert.That(first.IsSpecializationOf(variant, firstBase), Is.True);
            currentSource = nextBase.OwnerPrefab;
            StyleConsumerRecipeLookup next = CreateLookup();

            Assert.That(next.IsSpecializationOf(variant, firstBase), Is.False);
            Assert.That(next.IsSpecializationOf(variant, nextBase), Is.True);
            Assert.That(variant.BaseRecipe, Is.Null);
        }

        [Test]
        public void ExplicitBaseRecipeRemainsAuthoritativeEvenWhenUnregistered()
        {
            PrefabStyleRecipe inferred = CreateRecipeWithOwner("Inferred");
            PrefabStyleRecipe explicitBase = CreateRecipeWithOwner("Explicit");
            PrefabStyleRecipe variant = CreateRecipeWithOwner("Variant");
            SetPrivateField(variant, "_baseRecipe", explicitBase);
            var relationships = new StyleRecipeRelationships(new[] { inferred, variant },
                owner => owner == variant.OwnerPrefab ? inferred.OwnerPrefab : null);

            Assert.That(relationships.GetBaseRecipe(variant), Is.SameAs(explicitBase));
            Assert.That(relationships.IsSpecializationOf(variant, inferred), Is.False);
        }

        [Test]
        public void AmbiguousRegisteredAncestorStopsAutomaticInference()
        {
            PrefabStyleRecipe root = CreateRecipeWithOwner("Root");
            PrefabStyleRecipe ambiguous = CreateRecipeWithOwner("Ambiguous");
            PrefabStyleRecipe duplicate = CreateRecipe();
            SetPrivateField(duplicate, "_ownerPrefab", ambiguous.OwnerPrefab);
            PrefabStyleRecipe variant = CreateRecipeWithOwner("Variant");
            var relationships = new StyleRecipeRelationships(new[] { root, ambiguous, duplicate, variant }, owner =>
            {
                if (owner == variant.OwnerPrefab)
                {
                    return ambiguous.OwnerPrefab;
                }

                return owner == ambiguous.OwnerPrefab ? root.OwnerPrefab : null;
            });

            Assert.That(relationships.GetBaseRecipe(variant), Is.Null);
            Assert.That(relationships.GetBaseRecipe(ambiguous), Is.Null);
            Assert.That(relationships.IsSpecializationOf(variant, root), Is.False);
        }

        [Test]
        public void MissingAndCyclicSourceAncestryDoesNotCreateSelfSpecialization()
        {
            PrefabStyleRecipe variant = CreateRecipeWithOwner("Variant");
            PrefabStyleRecipe missing = CreateRecipe();
            var intermediate = new GameObject("Intermediate");
            _objects.Add(intermediate);
            var relationships = new StyleRecipeRelationships(new[] { variant, missing, null },
                owner => owner == variant.OwnerPrefab ? intermediate : variant.OwnerPrefab);

            Assert.That(relationships.GetBaseRecipe(variant), Is.Null);
            Assert.That(relationships.GetBaseRecipe(missing), Is.Null);
            Assert.That(relationships.GetBaseRecipe(null), Is.Null);
            Assert.That(relationships.IsSpecializationOf(variant, variant), Is.False);
            Assert.That(relationships.IsSpecializationOf(variant, null), Is.False);
            Assert.That(new StyleRecipeRelationships(null).GetBaseRecipe(variant), Is.Null);
        }

        private PrefabStyleRecipe CreateRecipeWithOwner(string name)
        {
            PrefabStyleRecipe recipe = CreateRecipe();
            var owner = new GameObject(name);
            _objects.Add(owner);
            SetPrivateField(recipe, "_ownerPrefab", owner);
            return recipe;
        }

        private PrefabStyleRecipe CreateRecipe()
        {
            var recipe = ScriptableObject.CreateInstance<PrefabStyleRecipe>();
            _objects.Add(recipe);
            return recipe;
        }

        private static PrefabTargetReference CreateTarget(string displayPath)
        {
            Assert.That(GlobalObjectId.TryParse(CapturedId, out _), Is.True);
            var target = new PrefabTargetReference();
            target.Configure(CapturedId, displayPath, typeof(TextMeshProUGUI).FullName, PrefabTargetKind.Text);
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
