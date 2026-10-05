namespace SharpProof.Contracts;

internal sealed class ContractApiSymbols(
    ContractClauseSymbols clauses,
    IMethodSymbol result,
    IMethodSymbol old,
    ContractSelectionInventory selections)
{
    internal ContractClauseSymbols Clauses { get; } = clauses;
    internal IMethodSymbol Result { get; } = result;
    internal IMethodSymbol Old { get; } = old;
    internal ContractSelectionInventory Selections { get; } = selections;

    internal static ContractApiSymbols? TryCreate(Compilation compilation)
    {
        var identity = ContractApiIdentityResolver.ForCompilation(compilation);
        var clauses = identity.Contract is { } contract
            ? new ContractClauseSymbols(contract)
            : null;
        if (clauses == null ||
            identity.Result is not { } result ||
            identity.Old is not { } old)
        {
            return null;
        }

        var selections =
            ContractSelectionInventory.ForCompilation(compilation);

        return new ContractApiSymbols(
            clauses,
            result,
            old,
            selections);
    }

    internal bool IsResult(IMethodSymbol method)
    {
        return SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Result);
    }

    internal bool IsOld(IMethodSymbol method)
    {
        return SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Old);
    }

}

internal sealed class ContractClauseSymbols(INamedTypeSymbol contractType)
{
    internal INamedTypeSymbol ContractType { get; } = contractType;

    internal static ContractClauseSymbols? TryCreate(Compilation compilation)
    {
        return ContractApiIdentityResolver.ForCompilation(compilation).Contract
            is { } contract
            ? new(contract)
            : null;
    }

    internal BoundContractKind? GetClauseKind(IMethodSymbol method)
    {
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
        {
            return null;
        }

        return Enum.TryParse<BoundContractKind>(
            ContractApiClauseProjection.GetClauseRole(definition.Name).ToString(),
            out var kind)
            ? kind
            : null;
    }
}
