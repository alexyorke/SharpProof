namespace SharpProof.Effects;

internal static class MonitorFacts
{
    internal static bool IsMonitorMethod(
        IMethodSymbol method,
        INamedTypeSymbol? monitorType)
    {
        return monitorType != null &&
            SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                monitorType.OriginalDefinition);
    }
}
