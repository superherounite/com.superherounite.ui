using System;
using System.Collections.Generic;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor.Tests
{
    /// <summary>Runs the original 12 smoke tests from Unity's executeMethod CLI.</summary>
    public static class StyleRecipeProcessorBatchRunner
    {
        [Obsolete("Runs only the original 12 smoke tests; use Tools~/Validate-Package.ps1 for the complete suite.")]
        public static void Run()
        {
            Debug.LogWarning(
                "Runs only the original 12 smoke tests; "
                + "use Tools~/Validate-Package.ps1 for the complete suite.");
            var tests = new (string Name, Action<StyleRecipeProcessorTests> Run)[]
            {
                (
                    nameof(StyleRecipeProcessorTests.PreviewAndApplyAreReadOnlyAndIdempotent),
                    test => test.PreviewAndApplyAreReadOnlyAndIdempotent()),
                (
                    nameof(StyleRecipeProcessorTests.ApplyRejectsAStalePreview),
                    test => test.ApplyRejectsAStalePreview()),
                (
                    nameof(StyleRecipeProcessorTests.DuplicatePropertyOwnershipIsAnError),
                    test => test.DuplicatePropertyOwnershipIsAnError()),
                (
                    nameof(StyleRecipeProcessorTests.ConsumerOverrideIsAnError),
                    test => test.ConsumerOverrideIsAnError()),
                (
                    nameof(StyleRecipeProcessorTests.MissingTargetIsAnError),
                    test => test.MissingTargetIsAnError()),
                (
                    nameof(StyleRecipeProcessorTests.ConsumerOverrideThroughVariantIsAnError),
                    test => test.ConsumerOverrideThroughVariantIsAnError()),
                (
                    nameof(StyleRecipeProcessorTests.ExplicitVariantSpecializationCanReplaceBaseProperty),
                    test => test.ExplicitVariantSpecializationCanReplaceBaseProperty()),
                (
                    nameof(StyleRecipeProcessorTests.VariantOwnerWithConsumerCanReachReady),
                    test => test.VariantOwnerWithConsumerCanReachReady()),
                (
                    nameof(StyleRecipeProcessorTests.VariantAddedTargetCanResolveInConsumer),
                    test => test.VariantAddedTargetCanResolveInConsumer()),
                (
                    nameof(StyleRecipeProcessorTests.OwnerWithNestedPrefabDoesNotValidateItsNestedSourceAsAnotherOwner),
                    test => test.OwnerWithNestedPrefabDoesNotValidateItsNestedSourceAsAnotherOwner()),
                (
                    nameof(StyleRecipeProcessorTests.InvalidPixelsPerUnitMultiplierIsAnError),
                    test => test.InvalidPixelsPerUnitMultiplierIsAnError()),
                (
                    nameof(StyleRecipeProcessorTests.GraphicColorRejectsSpecializedTmpColor),
                    test => test.GraphicColorRejectsSpecializedTmpColor()),
            };
            var failures = new List<string>();
            foreach ((string name, Action<StyleRecipeProcessorTests> run) in tests)
            {
                var fixture = new StyleRecipeProcessorTests();
                try
                {
                    fixture.SetUp();
                    run(fixture);
                    Debug.Log($"Super Hero UI test passed: {name}");
                }
                catch (Exception exception)
                {
                    failures.Add($"{name}: {exception}");
                }
                finally
                {
                    fixture.TearDown();
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Super Hero UI original smoke tests failed ({failures.Count}/{tests.Length}).\n"
                    + string.Join("\n", failures));
            }

            Debug.Log($"Super Hero UI original smoke tests passed ({tests.Length}/{tests.Length}).");
        }
    }
}
