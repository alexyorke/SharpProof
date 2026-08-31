namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class MixedOwnershipFlowCaptureRegressionTests
{
    [Test]
    public void ConditionalFreshAndParameterReceiverRetainsCallerOwnership()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                public static void Mutate(
                    Box parameter,
                    bool useFresh) {
                    (useFresh ? new Box() : parameter).Value = 1;
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Mutate");
        var result = new EffectAnalysisSession(compilation).Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.True,
                "the parameter branch of the merged capture is caller-owned");
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
