namespace SharpProof.Contracts;

internal sealed class ContractApiSymbols(
    ContractClauseSymbols clauses,
    IMethodSymbol result,
    IMethodSymbol old,
    ContractSelectionInventory selections) {
    private ContractClauseSymbols Clauses { get; } = clauses;
    internal IMethodSymbol Result { get; } = result;
    internal IMethodSymbol Old { get; } = old;
    internal ContractSelectionInventory Selections { get; } = selections;

    internal static ContractApiSymbols? TryCreate(Compilation compilation) {
        var clauses = ContractClauseSymbols.TryCreate(compilation);
        var selections =
            ContractSelectionInventory.ForCompilation(compilation);
        if (clauses == null || selections.ContractFor == null ||
            selections.NotNull == null || selections.Positive == null ||
            selections.InRange == null)
            return null;

        var result = FindGenericIntrinsic(clauses.ContractType, "Result", 0);
        var old = FindGenericIntrinsic(clauses.ContractType, "Old", 1);
        if (result == null || old == null)
            return null;
        return new ContractApiSymbols(
            clauses,
            result,
            old,
            selections);
    }

    internal BoundContractKind? GetClauseKind(IMethodSymbol method) =>
        Clauses.GetClauseKind(method);

    internal bool IsResult(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Result);

    internal bool IsOld(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Old);

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
