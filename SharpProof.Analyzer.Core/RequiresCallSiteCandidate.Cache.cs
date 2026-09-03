namespace SharpProof.Analyzer;

internal readonly partial record struct RequiresCallSiteCandidate
{
    internal IMethodSymbol? ResolvedTargetMethod
    {
        get;
        init;
    }
}
