namespace SharpProof.CompilerArtifact;

internal static class CompilerSummaryProofLabel
{
    internal static string Create(
        CompilerSummaryOrigin origin,
        string callIdentity,
        string evidenceSha256,
        string evidenceIdentity,
        ImmutableArray<CompilerPreparedSummaryEvidence> dependencies)
    {
        var prefix = origin switch
        {
            CompilerSummaryOrigin.Source => "source-summary",
            CompilerSummaryOrigin.ImplementationIl => "il-summary",
            CompilerSummaryOrigin.SpecificationPack => "spec-pack",
            _ => string.Empty
        };
        if (prefix.Length == 0)
        {
            return string.Empty;
        }

        var evidencePrefix = origin == CompilerSummaryOrigin.SpecificationPack
            ? prefix + ":" + evidenceIdentity
            : prefix;
        var dependencyLabel = dependencies.IsDefaultOrEmpty
            ? string.Empty
            : ":deps=" + string.Join(
                ";",
                dependencies.Select(dependency =>
                {
                    var dependencyPrefix = dependency.Origin switch
                    {
                        CompilerSummaryOrigin.Source => "source-summary",
                        CompilerSummaryOrigin.ImplementationIl => "il-summary",
                        CompilerSummaryOrigin.SpecificationPack =>
                            "spec-pack:" + dependency.EvidenceIdentity,
                        _ => string.Empty
                    };
                    return dependencyPrefix + ":" + dependency.CallIdentity +
                        ":" + dependency.EvidenceSha256;
                }));
        return evidencePrefix + ":" + callIdentity + ":" + evidenceSha256 +
            dependencyLabel;
    }

    internal static string Create(CompilerPreparedSummaryCall summary)
    {
        return Create(
            summary.Origin,
            summary.CallIdentity,
            summary.EvidenceSha256,
            summary.EvidenceIdentity,
            summary.DependencyEvidence);
    }
}
