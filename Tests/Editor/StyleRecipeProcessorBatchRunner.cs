using System;
using System.Collections.Generic;

using UnityEngine;

namespace SuperHeroUnite.UI.Editor.Tests
{
    /// <summary>Runs the synchronous package tests from Unity's executeMethod CLI.</summary>
    public static class StyleRecipeProcessorBatchRunner
    {
        public static void Run()
        {
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
                    nameof(StyleRecipeProcessorTests.VariantOwnerWithConsumerCanReachReady),
                    test => test.VariantOwnerWithConsumerCanReachReady()),
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
                    $"Super Hero UI tests failed ({failures.Count}/{tests.Length}).\n"
                    + string.Join("\n", failures));
            }

            Debug.Log($"Super Hero UI tests passed ({tests.Length}/{tests.Length}).");
        }
    }
}
