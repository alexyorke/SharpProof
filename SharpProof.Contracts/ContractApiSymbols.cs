namespace SharpProof.Contracts;

internal sealed class ContractApiSymbols(
    ContractClauseSymbols clauses,
    IMethodSymbol result,
    IMethodSymbol old,
    INamedTypeSymbol contractFor,
    INamedTypeSymbol notNull,
    INamedTypeSymbol positive,
    INamedTypeSymbol inRange,
    INamedTypeSymbol? pure) {
    private ContractClauseSymbols Clauses { get; } = clauses;
    internal IMethodSymbol Result { get; } = result;
    internal IMethodSymbol Old { get; } = old;
    internal INamedTypeSymbol ContractFor { get; } = contractFor;
    internal INamedTypeSymbol NotNull { get; } = notNull;
    internal INamedTypeSymbol Positive { get; } = positive;
    internal INamedTypeSymbol InRange { get; } = inRange;
    internal INamedTypeSymbol? Pure { get; } = pure;

    internal static ContractApiSymbols? TryCreate(Compilation compilation) {
        var clauses = ContractClauseSymbols.TryCreate(compilation);
        var contractFor = compilation.GetTypeByMetadataName(
            ContractForSymbolMatcher.AttributeMetadataName);
        var notNull = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.NotNullAttribute");
        var positive = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.PositiveAttribute");
        var inRange = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.InRangeAttribute");
        var pure = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.PureAttribute");
        if (clauses == null || contractFor == null || notNull == null ||
            positive == null || inRange == null)
            return null;

        var result = FindGenericIntrinsic(clauses.ContractType, "Result", 0);
        var old = FindGenericIntrinsic(clauses.ContractType, "Old", 1);
        if (result == null || old == null)
            return null;
        return new ContractApiSymbols(
            clauses,
            result,
            old,
            contractFor,
            notNull,
            positive,
            inRange,
            pure);
    }

    internal BoundContractKind? GetClauseKind(IMethodSymbol method) =>
        Clauses.GetClauseKind(method);

    internal bool IsResult(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Result);

    internal bool IsOld(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Old);

    internal static bool IsAttribute(
        AttributeData attribute,
        INamedTypeSymbol? expected) =>
        expected != null &&
        attribute.AttributeClass != null &&
        SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass.OriginalDefinition,
            expected);

    private static IMethodSymbol? FindGenericIntrinsic(
        INamedTypeSymbol contract,
        string name,
        int parameterCount) =>
        contract.GetMembers(name)
            .OfType<IMethodSymbol>()
            .SingleOrDefault(method =>
                method.IsStatic &&
                method.Arity == 1 &&
                method.Parameters.Length == parameterCount);
}

internal sealed class ContractClauseSymbols(INamedTypeSymbol contractType) {
    internal INamedTypeSymbol ContractType { get; } = contractType;

    internal static ContractClauseSymbols? TryCreate(Compilation compilation) =>
        compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")
            is { } contract
            ? new(contract)
            : null;

    internal BoundContractKind? GetClauseKind(IMethodSymbol method) {
        var definition = method.OriginalDefinition;
        if (!SymbolEqualityComparer.Default.Equals(
                definition.ContainingType,
                ContractType) ||
            !definition.IsStatic ||
            definition.Arity != 0 ||
            !definition.ReturnsVoid ||
            definition.Parameters.Length != 1 ||
            definition.Parameters[0].Type.SpecialType !=
                SpecialType.System_Boolean)
            return null;
        return definition.Name switch {
            "Requires" => BoundContractKind.Requires,
            "Ensures" => BoundContractKind.Ensures,
            "Assume" => BoundContractKind.Assume,
            _ => null
        };
    }
}
