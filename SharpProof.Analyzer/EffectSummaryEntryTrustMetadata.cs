using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

internal sealed class EffectSummaryEntryTrustMetadata(
    SummaryAssemblyIdentity? assemblyIdentity,
    SummaryMethodIdentity? methodIdentity,
    EffectSummaryArtifactSource? artifactSource,
    int sourcePriority,
    int builtInSourcePriority,
    int additionalSourcePriority,
    string? sourcePath,
    EffectSummaryCompatibilityReporter? compatibilityReporter)
{
    internal SummaryAssemblyIdentity? AssemblyIdentity { get; } = assemblyIdentity;
    internal SummaryMethodIdentity? MethodIdentity { get; } = methodIdentity;
    internal int SourcePriority { get; } = sourcePriority;
    internal string? SourcePath { get; } = sourcePath;
    private EffectSummaryArtifactSource? ArtifactSource { get; } = artifactSource;
    private int BuiltInSourcePriority { get; } = builtInSourcePriority;
    private int AdditionalSourcePriority { get; } = additionalSourcePriority;
    private EffectSummaryCompatibilityReporter? CompatibilityReporter { get; } = compatibilityReporter;

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
