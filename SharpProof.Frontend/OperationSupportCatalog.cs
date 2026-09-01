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
    internal static bool IsSupported(
        OperationSupportStage stage,
        OperationKind kind)
    {
        return OperationSupportProjections.GetSupported(stage).Contains(kind);
    }

}
