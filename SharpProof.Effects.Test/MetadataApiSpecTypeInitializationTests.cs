using SharpProof.Ir;
using SharpProof.Specs;
using static SharpProof.Testing.ApiSpecTestFacets;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class MetadataApiSpecTypeInitializationTests
{
    [Test]
    public void ExactMetadataSpecsIncludeTypeInitializerEffects()
    {
        const string assemblyName =
            "MetadataApiSpecInitializationAssembly";
        var reference = EffectTestHost.EmitReference(
            """
            using System;
            using SharpProof.Attributes;

            [assembly: SharpProofTrusted(
                "reviewed metadata type initializer")]

            public sealed class MetadataTarget {
                public static int State;

                [EffectContract(
                    SharpProofEffect.WritesStaticState |
                    SharpProofEffect.Throws,
                    ThrownExceptions =
                        new Type[] { typeof(InvalidOperationException) },
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                static MetadataTarget() {
                    State = 1;
                    throw new InvalidOperationException();
                }

                public MetadataTarget() {
                }

                public static void Touch() {
                }
            }
            """,
            assemblyName);
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Call() =>
                    MetadataTarget.Touch();

                public static MetadataTarget Construct() =>
                    new MetadataTarget();
            }
            """,
            reference);
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Observed,
            "focused metadata API specification");
        var approvedAssemblies = ImmutableArray.Create(
            new ApiSpecAssemblyIdentity(assemblyName, string.Empty));
        var facets = NeutralFacets(evidence, includeTermination: true);
        var table = ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "test.metadata-target.touch",
                    "M:MetadataTarget.Touch",
                    "MetadataTarget",
                    SpecTargetMemberKind.Method,
                    "Touch",
                    true,
                    0,
                    null,
                    [],
                    null,
                    approvedAssemblies),
                facets,
                []),
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "test.metadata-target.ctor",
                    "M:MetadataTarget.#ctor",
                    "MetadataTarget",
                    SpecTargetMemberKind.Constructor,
                    ".ctor",
                    false,
                    0,
                    IrTypeKind.Reference,
                    [],
                    null,
                    approvedAssemblies),
                facets,
                [])
        ]);
        var session = new EffectAnalysisSession(compilation, table);

        foreach (var methodName in new[] { "Call", "Construct" })
        {
            var result = session.Analyze(
                EffectTestHost.RequireMethod(
                    compilation,
                    "Sample",
                    methodName));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Writes.Contains(
                        EffectRegionId.Static()),
                    Is.True,
                    methodName);
                Assert.That(
                    result.Summary.Writes.IsUnknown,
                    Is.False,
                    methodName);
                Assert.That(
                    result.Summary.Throws.Types.Select(static type =>
                        type.ToDisplayString()),
                    Does.Contain("System.TypeInitializationException"),
                    methodName);
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
                Assert.That(
                    result.Projection.IsComplete,
                    Is.True,
                    methodName);
            }
        }
    }
}
