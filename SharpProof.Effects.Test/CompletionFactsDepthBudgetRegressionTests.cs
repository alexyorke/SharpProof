namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CompletionFactsDepthBudgetRegressionTests
{
    [Test]
    public void DeepAcyclicCallChainAbstainsAndPreservesCallerSuffixEffects()
    {
        var methodCount =
            DefiniteOperationFacts.MaximumCompletionFactsDepth + 16;
        var methods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, methodCount).Select(index =>
                index + 1 < methodCount
                    ? $"private static void Step{index}() => Step{index + 1}();"
                    : $"private static void Step{index}() " +
                      "{ while (true) { } }"));
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample
            {
                public static int State;
            {{methods}}
                public static void Entry()
                {
                    Step0();
                    State++;
                }
            }
            """);
        var completion = EffectTestHost.CreateCompletionFacts(compilation);
        var firstStep = EffectTestHost.SampleMethod(compilation, "Step0");

        Assert.That(
            completion.MethodCanCompleteNormally(firstStep),
            Is.True,
            "Exhaustion must retain the possibility of normal completion.");

        var summary = EffectTestHost.AnalyzeSample(compilation, "Entry")
            .Summary;
        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.True,
            "A depth cutoff must not suppress later source effects.");
    }

    [Test]
    public void DeepOperationTreeAbstainsAndPreservesCallerSuffixEffects()
    {
        var operandCount =
            DefiniteOperationFacts.MaximumCompletionFactsDepth + 32;
        var expression = string.Join(
            " + ",
            Enumerable.Repeat("1", operandCount).Prepend("Never()"));
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            using System;
            public static class Sample
            {
                public static int State;
                private static int Never() =>
                    throw new InvalidOperationException();
                public static int Deep() => {{expression}};
                public static void Entry()
                {
                    Deep();
                    State++;
                }
            }
            """);
        var completion = EffectTestHost.CreateCompletionFacts(compilation);
        var deep = EffectTestHost.SampleMethod(compilation, "Deep");

        Assert.That(
            completion.MethodCanCompleteNormally(deep),
            Is.True,
            "An operation-tree cutoff must be treated as unknown.");

        var summary = EffectTestHost.AnalyzeSample(compilation, "Entry")
            .Summary;
        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.True,
            "An operation-tree cutoff must not suppress later source effects.");
    }
}
