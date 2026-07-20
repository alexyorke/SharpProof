namespace SharpProof.Symbolic.Ir;

internal delegate bool KnownApiLoweringHandler<TValue>(
    InvocationExpressionSyntax invocation,
    IMethodSymbol method,
    SymbolicLoweringContext context,
    out TValue value);

internal sealed class KnownApiLoweringDescriptor<TValue> {
    public KnownApiLoweringDescriptor(
        SpecialType containingTypeSpecialType,
        string methodName,
        KnownApiLoweringHandler<TValue> handler) {
        ContainingTypeSpecialType = containingTypeSpecialType;
        MethodName = methodName;
        Handler = handler;
    }

    public KnownApiLoweringDescriptor(
        string containingTypeMetadataName,
        string methodName,
        KnownApiLoweringHandler<TValue> handler) {
        ContainingTypeMetadataName = containingTypeMetadataName;
        MethodName = methodName;
        Handler = handler;
    }

    public SpecialType ContainingTypeSpecialType { get; }

    public string? ContainingTypeMetadataName { get; }

    public string MethodName { get; }

    public KnownApiLoweringHandler<TValue> Handler { get; }

    public bool Matches(IMethodSymbol method) {
        if (!string.Equals(method.Name, MethodName, StringComparison.Ordinal)) return false;

        return ContainingTypeMetadataName == null
            ? method.ContainingType?.OriginalDefinition.SpecialType == ContainingTypeSpecialType
            : string.Equals(
                SymbolicTypeFacts.GetFullMetadataName(method.ContainingType),
                ContainingTypeMetadataName,
                StringComparison.Ordinal);
    }
}
