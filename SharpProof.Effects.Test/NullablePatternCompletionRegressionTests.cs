namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NullablePatternCompletionRegressionTests
{
    private static readonly Compilation SharedCompilation = CreateCompilation();

    [TestCase("RecursivePatternMayReturn", "AfterRecursivePattern")]
    [TestCase("ListPatternMayReturn", "AfterListPattern")]
    public void NullMismatchRetainsTheCallersSuffixEffect(
        string helperName,
        string callerName)
    {
        var compilation = SharedCompilation;
        var helper = EffectTestHost.SampleMethod(compilation, helperName);
        var caller = EffectTestHost.SampleMethod(compilation, callerName);
        var completion = EffectTestHost.CreateCompletionFacts(compilation);
        var result = EffectTestHost.AnalyzeSample(compilation, callerName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.MethodCanCompleteNormally(helper),
                Is.True,
                "the nullable null-mismatch path returns normally");
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "the caller reaches its suffix write when the helper returns");
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            #nullable enable

            public readonly struct RecursivePatternBomb {
                public int Value { get { while (true) { } } }
            }

            public readonly struct ListPatternBomb {
                public int Length { get { while (true) { } } }
                public int this[int index] => 0;
            }

            public static class Sample {
                private static int state;

                private static bool RecursivePatternMayReturn(
                    RecursivePatternBomb? value) =>
                    value is { Value: 0 };

                private static bool ListPatternMayReturn(
                    ListPatternBomb? value) =>
                    value is [];

                public static void AfterRecursivePattern(
                    RecursivePatternBomb? value) {
                    _ = RecursivePatternMayReturn(value);
                    state++;
                }

                public static void AfterListPattern(
                    ListPatternBomb? value) {
                    _ = ListPatternMayReturn(value);
                    state++;
                }
            }
            """);
    }
}
