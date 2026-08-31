namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NullableBoxingRoundTripRegressionTests
{
    [Test]
    public void BoxedNullableRoundTripsPreserveTheirUnboxingType()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int? Unknown(int? value) =>
                    (int?)(object?)value;

                public static int? Present() {
                    int? value = 1;
                    return (int?)(object)value;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var unknown = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Unknown"));
        var present = session.Analyze(EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Present"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unknown.Summary.Throws.IsEmpty, Is.True);
            Assert.That(
                unknown.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Unknown));
            Assert.That(present.Summary.Throws.IsEmpty, Is.True);
            Assert.That(
                present.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
        }
    }
}
