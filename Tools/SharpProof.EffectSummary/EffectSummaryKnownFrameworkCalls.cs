internal static class EffectSummaryKnownFrameworkCalls
{
    internal static bool IsArrayDataReference(string callSymbol)
    {
        return callSymbol.StartsWith(
            "System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(",
            StringComparison.Ordinal);
    }

    internal static bool IsByRefLikeRuntimeTypeHelper(string callSymbol)
    {
        return callSymbol.StartsWith(
                   "System.ThrowHelper.ThrowArrayTypeMismatchException()",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith(
                   "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("System.Type.get_IsValueType()", StringComparison.Ordinal) ||
               callSymbol.StartsWith(
                   "System.Type.op_Inequality(System.Type, System.Type)",
                   StringComparison.Ordinal) ||
               callSymbol.StartsWith("object.GetType()", StringComparison.Ordinal);
    }
}
