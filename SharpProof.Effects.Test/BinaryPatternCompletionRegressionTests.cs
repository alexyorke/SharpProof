namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class BinaryPatternCompletionRegressionTests
{
    private static readonly Compilation SharedCompilation = CreateCompilation();

    [TestCase("OrPatternCanSkipDivergentRight")]
    [TestCase("AndPatternCanSkipDivergentRight")]
    public void ShortCircuitedPatternRetainsTheCallersSuffixEffect(
        string methodName)
    {
        var compilation = SharedCompilation;
        var method = EffectTestHost.SampleMethod(compilation, methodName);
        var completion = EffectTestHost.CreateCompletionFacts(compilation);
        var summary = EffectTestHost.AnalyzeSample(compilation, methodName).Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.MethodCanCompleteNormally(method),
                Is.True);
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            public sealed class PatternSource {
                public int Value { get { while (true) { } } }
            }

            public static class Sample {
                private static int state;

                public static void OrPatternCanSkipDivergentRight(
                    PatternSource value) {
                    _ = value is null or { Value: 0 };
                    state++;
                }

                public static void AndPatternCanSkipDivergentRight(
                    PatternSource value) {
                    _ = value is not null and { Value: 0 };
                    state++;
                }
            }
            """);
    }
}
