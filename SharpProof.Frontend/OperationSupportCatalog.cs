using System.Collections.Immutable;

namespace SharpProof.Frontend;

internal enum OperationSupportStage
{
    ContractExpressionLowering,
    EffectDiscovery
}

/// <summary>
/// Closed Roslyn operation support decisions shared by compiler-facing stages.
/// Shape and type checks remain with the stage that owns their semantics.
/// </summary>
internal static class OperationSupportCatalog
{
    private static readonly ImmutableHashSet<OperationKind>
        ContractExpression = ImmutableHashSet.CreateRange(
            OperationSupportCatalogData.ContractExpression);
    private static readonly ImmutableHashSet<OperationKind>
        EffectDiscovery = ImmutableHashSet.CreateRange(
            OperationSupportCatalogData.EffectDiscovery);

    internal static bool IsSupported(
        OperationSupportStage stage,
        OperationKind kind)
    {
        return stage switch
        {
            OperationSupportStage.ContractExpressionLowering =>
                ContractExpression.Contains(kind),
            OperationSupportStage.EffectDiscovery =>
                EffectDiscovery.Contains(kind),
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Unknown operation support stage.")
        };
    }

}
