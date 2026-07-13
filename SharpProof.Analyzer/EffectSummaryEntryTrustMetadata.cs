using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

internal sealed class EffectSummaryEntryTrustMetadata
{
    internal EffectSummaryEntryTrustMetadata(
        SummaryAssemblyIdentity? assemblyIdentity,
        SummaryMethodIdentity? methodIdentity,
        EffectSummaryArtifactSource? artifactSource,
        int sourcePriority,
        int builtInSourcePriority,
        int additionalSourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter)
    {
        AssemblyIdentity = assemblyIdentity;
        MethodIdentity = methodIdentity;
        ArtifactSource = artifactSource;
        SourcePriority = sourcePriority;
        BuiltInSourcePriority = builtInSourcePriority;
        AdditionalSourcePriority = additionalSourcePriority;
        SourcePath = sourcePath;
        CompatibilityReporter = compatibilityReporter;
    }

    internal SummaryAssemblyIdentity? AssemblyIdentity { get; }

    internal SummaryMethodIdentity? MethodIdentity { get; }

    internal int SourcePriority { get; }

    internal string? SourcePath { get; }

    private EffectSummaryArtifactSource? ArtifactSource { get; }

    private int BuiltInSourcePriority { get; }

    private int AdditionalSourcePriority { get; }

    private EffectSummaryCompatibilityReporter? CompatibilityReporter { get; }

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
