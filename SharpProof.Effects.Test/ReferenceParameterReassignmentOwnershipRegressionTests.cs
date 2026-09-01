namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ReferenceParameterReassignmentOwnershipRegressionTests
{
    [Test]
    public void ReassignedParameterRetainsReplacementOwnership()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box
            {
                public int Value;
            }

            public static class Sample
            {
                private static void ReassignThenMutate(
                    Box target,
                    Box replacement)
                {
                    target = replacement;
                    target.Value = 1;
                }

                public static void Invoke(Box value) =>
                    ReassignThenMutate(new Box(), value);
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var reassignment = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "ReassignThenMutate"));
        var invocation = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "Invoke"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                reassignment.Summary.Writes.Contains(
                    EffectRegionId.Parameter(1)),
                Is.True,
                "the callee mutates the replacement parameter");
            Assert.That(
                invocation.Summary.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.True,
                "the replacement remains caller-owned after remapping");
            Assert.That(invocation.Summary.Writes.IsUnknown, Is.False);
            Assert.That(
                invocation.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                EffectContractMappings.IsObservablePure(invocation.Summary),
                Is.False);
        }
    }
}
