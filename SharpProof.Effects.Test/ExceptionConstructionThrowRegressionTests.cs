using SharpProof.Ir;
using SharpProof.Specs;

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

    [Test]
    public void MetadataExceptionConstructorCanReachMatchingCatch()
    {
        const string assemblyName = "MetadataExceptionAssembly";
        var reference = EffectTestHost.EmitReference(
            """
            public sealed class MetadataException : System.Exception {
                public MetadataException() {
                }
            }
            """,
            assemblyName);
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

                public static void Construct() {
                    try {
                        _ = new MetadataException();
                    }
                    catch (System.InvalidOperationException) {
                        s_state++;
                    }
                }
            }
            """,
            reference);
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Observed,
            "metadata exception constructor regression");
        var table = ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "test.metadata-exception.ctor",
                    "M:MetadataException.#ctor",
                    "MetadataException",
                    SpecTargetMemberKind.Constructor,
                    ".ctor",
                    false,
                    0,
                    IrTypeKind.Reference,
                    [],
                    null,
                    [new ApiSpecAssemblyIdentity(
                        assemblyName,
                        string.Empty)]),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(
                        SpecAllocationBehavior.None,
                        evidence),
                    new SpecThrowFacet(
                        SpecThrowBehavior.MayThrow,
                        ["System.InvalidOperationException"],
                        evidence),
                    new SpecNullnessFacet(
                        SpecNullness.NotApplicable,
                        evidence),
                    new SpecCardinalityFacet(
                        SpecCardinality.NotApplicable,
                        null,
                        evidence),
                    new SpecTerminationFacet(
                        SpecTerminationBehavior.Terminates,
                        evidence)),
                [])
        ]);

        var result = new EffectAnalysisSession(compilation, table).Analyze(
            EffectTestHost.SampleMethod(compilation, "Construct"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True,
                "the metadata constructor can transfer control to the catch");
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }
}
