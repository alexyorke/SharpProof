using SharpProof.Specs;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

internal static class WorkerApiSpecTestFixtures
{
    internal static ApiSpecTemplate CreateTemplate(
        string targetId,
        string metadataName,
        string containingType,
        string evidenceIdentity,
        IrTypeKind resultType,
        SpecNullness nullness,
        SpecCardinality cardinality,
        IEnumerable<SpecTermDeclaration>? postconditions = null)
    {
        System.ArgumentNullException.ThrowIfNull(containingType);
        System.ArgumentNullException.ThrowIfNull(evidenceIdentity);
        System.ArgumentNullException.ThrowIfNull(metadataName);
        System.ArgumentNullException.ThrowIfNull(targetId);
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Documented,
            evidenceIdentity);
        return ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    targetId,
                    metadataName,
                    containingType,
                    SpecTargetMemberKind.Method,
                    "Result",
                    true,
                    0,
                    null,
                    [],
                    resultType,
                    [new ApiSpecAssemblyIdentity("Test", string.Empty)]),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(
                        SpecAllocationBehavior.Unknown,
                        evidence),
                    new SpecThrowFacet(
                        SpecThrowBehavior.DoesNotThrow,
                        [],
                        evidence),
                    new SpecNullnessFacet(nullness, evidence),
                    new SpecCardinalityFacet(
                        cardinality,
                        null,
                        evidence)),
                [.. (postconditions ?? []).Select(
                    condition => condition is null ? throw new System.ArgumentNullException(nameof(condition)) : (new SpecPostconditionDeclaration(
                        condition,
                        evidence)))])
        ]).Templates.Single();
    }
}
