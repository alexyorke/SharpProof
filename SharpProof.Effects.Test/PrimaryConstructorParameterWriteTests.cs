namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class PrimaryConstructorParameterWriteTests
{
    [Test]
    public void AssignmentWritesReceiverBackedPrimaryConstructorStorage()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample(int state) {
                public void Assign(int value) {
                    state = value;
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            "Assign");

        var result = new EffectAnalysisSession(compilation)
            .Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Regions,
                Is.EqualTo(new[] { EffectRegionId.Receiver }));
            Assert.That(result.Summary.Reads.IsEmpty, Is.True);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                result.Projection.Effects,
                Is.EqualTo(SharpProofEffect.WritesReceiverState));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }
}
