namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class BranchingExpressionEffectRegressionTests
{
    [Test]
    public void TerminalConditionalArmDoesNotSuppressReachableSiblingEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static int s_state;

                public static int Evaluate(bool condition) =>
                    condition ? Stop() : Mutate();

                private static int Stop() =>
                    throw new InvalidOperationException();

                private static int Mutate() => ++s_state;
            }
            """);

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "Evaluate"))
            .Summary;

        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
    }

    [TestCase("ShortCircuitAnd")]
    [TestCase("ShortCircuitOr")]
    [TestCase("NonNullCoalesce")]
    public void InfeasibleBranchEffectsAreNotScanned(string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

                public static bool ShortCircuitAnd() {
                    var condition = false;
                    return condition && MutateBoolean();
                }

                public static bool ShortCircuitOr() {
                    var condition = true;
                    return condition || MutateBoolean();
                }

                public static object NonNullCoalesce() {
                    object? value = new object();
                    return value ?? MutateObject();
                }

                private static bool MutateBoolean() {
                    s_state++;
                    return true;
                }

                private static object MutateObject() {
                    s_state++;
                    return new object();
                }
            }
            """);

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                methodName))
            .Summary;

        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.False,
            methodName);
    }
}
