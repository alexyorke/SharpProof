namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NullableValueTypeNullnessRegressionTests
{
    [Test]
    public void NullableCoalesceIncludesReachableNullArmEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

                public static int Coalesce(int? value) =>
                    value ?? (s_state = 1);
            }
            """);

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "Coalesce"))
            .Summary;

        Assert.That(
            summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
    }
}
