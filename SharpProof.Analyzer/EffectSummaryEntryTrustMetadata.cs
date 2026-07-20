namespace SharpProof.Analyzer;

internal sealed record EffectSummaryEntryTrustMetadata(
    SummaryAssemblyIdentity? AssemblyIdentity,
    SummaryMethodIdentity? MethodIdentity,
    EffectSummaryArtifactSource? ArtifactSource,
    int SourcePriority,
    int BuiltInSourcePriority,
    int AdditionalSourcePriority,
    string? SourcePath,
    EffectSummaryCompatibilityReporter? CompatibilityReporter)
{
    internal bool IsTrustedFor(
        IMethodSymbol methodSymbol,
        ActualAssemblyIdentity? actualAssemblyIdentity,
        ActualMethodIdentity? actualMethodIdentity,
        string displaySymbol)
    {
        return methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true &&
               IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity, displaySymbol);
    }

    internal bool IsTrustedFor(
        ActualAssemblyIdentity? actualAssemblyIdentity,
        ActualMethodIdentity? actualMethodIdentity,
        string displaySymbol)
    {
        return EffectSummaryEntryTrustEvaluator.IsTrusted(
            AssemblyIdentity,
            ArtifactSource,
            MethodIdentity,
            actualAssemblyIdentity,
            actualMethodIdentity,
            SourcePriority == BuiltInSourcePriority,
            SourcePriority == AdditionalSourcePriority ? CompatibilityReporter : null,
            SourcePath,
            displaySymbol);
    }
}
