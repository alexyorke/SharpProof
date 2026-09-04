namespace SharpProof.Effects;

internal static class MonitorFacts
{
    internal static bool IsExplicitMonitorCall(
        IInvocationOperation invocation,
        INamedTypeSymbol? monitorType)
    {
        return !invocation.IsImplicit &&
            invocation.Instance == null &&
            !invocation.Arguments.IsDefaultOrEmpty &&
            invocation.TargetMethod.Name is
                "Enter" or "Exit" or "Pulse" or "PulseAll" or
                "TryEnter" or "Wait" &&
            IsMonitorMethod(invocation.TargetMethod, monitorType) &&
            invocation.Arguments.All(static argument =>
                DefiniteOperationFacts.IsHarmlessValue(argument.Value)) &&
            DefiniteOperationFacts.IsDefinitelyNonNull(
                invocation.Arguments[0].Value);
    }

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
