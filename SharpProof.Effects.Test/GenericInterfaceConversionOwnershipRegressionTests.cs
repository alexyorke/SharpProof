namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class GenericInterfaceConversionOwnershipRegressionTests
{
    [Test]
    public void GenericInterfaceConversionRetainsPossibleCallerOwnership()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public interface IState {
            }

            public sealed class State : IState {
            }

            public static class ExternalBoundary {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.WritesArgumentState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public static void Mutate(IState value) {
                }
            }
            """,
            "GenericInterfaceBoundaryAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Invoke<T>(T value)
                    where T : IState =>
                    ExternalBoundary.Mutate(value);
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                "Invoke"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsUnknown, Is.False);
            Assert.That(
                result.Summary.Writes.Regions,
                Does.Contain(EffectRegionId.Parameter(0)),
                "T can be a reference type whose interface conversion preserves identity.");
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False);
            Assert.That(
                result.Projection.Effects,
                Is.EqualTo(
                    SharpProofEffect.WritesArgumentState |
                    SharpProofEffect.Allocates));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }
}
