using SharpProof.Specs;

namespace SharpProof.Testing;

internal static class ApiSpecTestFacets
{
    internal static ApiSpecFacets NeutralFacets(
        SpecEvidence evidence,
        bool includeTermination = false)
    {
        return new(
            new SpecEffectFacet(SpecEffect.None, evidence),
            new SpecAllocationFacet(SpecAllocationBehavior.None, evidence),
            new SpecThrowFacet(SpecThrowBehavior.DoesNotThrow, [], evidence),
            new SpecNullnessFacet(SpecNullness.NotApplicable, evidence),
            new SpecCardinalityFacet(
                SpecCardinality.NotApplicable,
                null,
                evidence),
            includeTermination
                ? new SpecTerminationFacet(
                    SpecTerminationBehavior.Terminates,
                    evidence)
                : null);
    }
}
