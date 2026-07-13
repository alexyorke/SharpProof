namespace SharpProof.Analyzer;

internal static class EffectSummaryEntryTrustEvaluator
{
    internal static bool IsTrusted(
        SummaryAssemblyIdentity? assemblyIdentity,
        EffectSummaryArtifactSource? artifactSource,
        SummaryMethodIdentity? methodIdentity,
        ActualAssemblyIdentity? actualAssemblyIdentity,
        ActualMethodIdentity? actualMethodIdentity,
        bool allowBuiltInMetadataTokenFallback,
        EffectSummaryCompatibilityReporter? compatibilityReporter,
        string? sourcePath,
        string displaySymbol)
    {
        var assemblyCompatibility = assemblyIdentity?.GetCompatibility(actualAssemblyIdentity) ??
                                    EffectSummaryCompatibility.Incompatible(
                                        "effect_summary_incomplete_assembly_identity",
                                        "its assembly identity is missing");
        if (!assemblyCompatibility.IsCompatible)
            return Reject(assemblyCompatibility, compatibilityReporter, sourcePath, displaySymbol);

        var artifactSourceCompatibility = artifactSource?.GetCompatibility(actualAssemblyIdentity!) ??
                                          EffectSummaryCompatibility.Compatible;
        if (!artifactSourceCompatibility.IsCompatible)
            return Reject(artifactSourceCompatibility, compatibilityReporter, sourcePath, displaySymbol);

        var methodCompatibility = methodIdentity?.GetCompatibility(actualMethodIdentity) ??
                                  EffectSummaryCompatibility.Incompatible(
                                      "effect_summary_incomplete_method_identity",
                                      "its method identity is missing");
        if (methodCompatibility.IsCompatible) return true;

        if (allowBuiltInMetadataTokenFallback &&
            methodIdentity?.MatchesMetadataToken(actualMethodIdentity) == true)
            return true;

        return Reject(methodCompatibility, compatibilityReporter, sourcePath, displaySymbol);
    }

    private static bool Reject(
        EffectSummaryCompatibility compatibility,
        EffectSummaryCompatibilityReporter? compatibilityReporter,
        string? sourcePath,
        string displaySymbol)
    {
        compatibilityReporter?.Report(sourcePath ?? string.Empty, displaySymbol, compatibility);
        return false;
    }
}
