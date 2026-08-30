namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ExceptionConstructionThrowRegressionTests
{
    [Test]
    public void ConstructorEffectsPrecedeExplicitThrow()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class ExternalException : System.Exception {
            }
            """);
        var constructorException = EffectTestHost.RequireType(
            compilation,
            "System.ArgumentException");
        var explicitException = EffectTestHost.RequireType(
            compilation,
            "ExternalException");
        var construction = new EffectSummary(
            EffectRegionSet.Create(EffectRegionId.Ambient),
            EffectRegionSet.Create(EffectRegionId.Static()),
            EffectAllocationKind.Managed,
            new EffectCapabilitySet(EffectCapabilityKind.IO),
            EffectThrowSet.Create([constructorException]),
            EffectTermination.Terminates,
            EffectCompleteness.Complete,
            EffectUncertainty.DirectCall);

        var summary = EffectSummaryOperations.ExceptionConstructionThrow(
            construction,
            EffectThrowSet.Create([explicitException]));
        var exceptionNames = summary.Throws.Types.Select(static type =>
            type.ToDisplayString()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Reads.Contains(EffectRegionId.Ambient),
                Is.True);
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                summary.Capabilities.Contains(EffectCapabilityKind.IO),
                Is.True);
            Assert.That(
                exceptionNames,
                Has.Length.EqualTo(2));
            Assert.That(exceptionNames, Does.Contain("ExternalException"));
            Assert.That(
                exceptionNames,
                Does.Contain("System.ArgumentException"));
            Assert.That(summary.Throws.IncludesUnknown, Is.False);
            Assert.That(
                summary.Termination,
                Is.EqualTo(EffectTermination.Unknown));
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                summary.Uncertainty,
                Is.EqualTo(EffectUncertainty.DirectCall));
        }
    }
}
