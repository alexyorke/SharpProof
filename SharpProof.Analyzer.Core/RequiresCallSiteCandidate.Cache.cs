namespace SharpProof.Analyzer;

internal readonly partial record struct RequiresCallSiteCandidate
{
    internal PotentialRequiresCallOrigin? OriginKind { get; init; }
    internal int CallRoleIndex { get; init; } = -1;
    internal bool MergedRoleCoverage { get; init; }

    internal IMethodSymbol? ResolvedTargetMethod
    {
        get;
        init;
    }
}
