namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class FalseSwitchGuardPatternRegressionTests
{
    private static readonly Compilation SharedCompilation = CreateCompilation();

    [TestCase("PropertyGetter")]
    [TestCase("Deconstruct")]
    [TestCase("ListLength")]
    [TestCase("ListIndexer")]
    public void FalseGuardRetainsMandatoryPatternEffectsInCompleteSummary(
        string methodName)
    {
        var compilation = SharedCompilation;
        var method = EffectTestHost.SampleMethod(compilation, methodName);

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(method)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                methodName);
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                methodName);
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            public static class EffectState {
                public static int Value;
            }

            public struct PropertySource {
                public int Value {
                    get {
                        EffectState.Value++;
                        return 0;
                    }
                }
            }

            public struct DeconstructSource {
                public void Deconstruct(out int value) {
                    EffectState.Value++;
                    value = 0;
                }
            }

            public struct ListLengthSource {
                public int Length {
                    get {
                        EffectState.Value++;
                        return 0;
                    }
                }

                public int this[int index] => 0;
            }

            public struct ListIndexerSource {
                public int Length => 1;

                public int this[int index] {
                    get {
                        EffectState.Value++;
                        return 0;
                    }
                }
            }

            public static class Sample {
                public static int PropertyGetter(PropertySource value) =>
                    value switch {
                        { Value: _ } when false => 0,
                        _ => 1
                    };

                public static int Deconstruct(DeconstructSource value) =>
                    value switch {
                        DeconstructSource(_) when false => 0,
                        _ => 1
                    };

                public static int ListLength(ListLengthSource value) =>
                    value switch {
                        [] when false => 0,
                        _ => 1
                    };

                public static int ListIndexer(ListIndexerSource value) =>
                    value switch {
                        [_] when false => 0,
                        _ => 1
                    };
            }
            """);
    }
}
