namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ByValueStructReferenceOwnershipRegressionTests
{
    [Test]
    public void TrustedWriteThroughReferenceFieldOfStructArgumentRemainsObservable()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public sealed class Cell
            {
                public int Value;
            }

            public struct Holder
            {
                public Cell Cell;
            }

            public static class ExternalBoundary
            {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.WritesArgumentState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public static void Mutate(Holder holder)
                {
                    holder.Cell.Value++;
                }
            }
            """,
            "StructReferenceBoundaryAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample
            {
                public static void Invoke(Holder holder) =>
                    ExternalBoundary.Mutate(holder);
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "Invoke"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(result.Summary.Writes.IsUnknown, Is.False);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False);
            Assert.That(
                result.Projection.Effects,
                Is.EqualTo(SharpProofEffect.WritesArgumentState));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }
}
