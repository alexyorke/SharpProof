namespace SharpProof.Contracts;

internal sealed class ContractApiSymbols(
    ContractClauseSymbols clauses,
    IMethodSymbol result,
    IMethodSymbol old,
    ContractSelectionInventory selections)
{
    private ContractClauseSymbols Clauses { get; } = clauses;
    internal IMethodSymbol Result { get; } = result;
    internal IMethodSymbol Old { get; } = old;
    internal ContractSelectionInventory Selections { get; } = selections;

    internal static ContractApiSymbols? TryCreate(Compilation compilation)
    {
        var clauses = ContractClauseSymbols.TryCreate(compilation);
        if (clauses == null)
        {
            return null;
        }

        var selections =
            ContractSelectionInventory.ForCompilation(compilation);

        IMethodSymbol? result = null;
        IMethodSymbol? old = null;
        foreach (var member in clauses.ContractType.GetMembers())
        {
            if (member is not IMethodSymbol method ||
                !method.IsStatic ||
                method.Arity != 1)
            {
                continue;
            }

            if (method.Name == ContractApiMetadata.ResultMethodName &&
                method.Parameters.Length == 0)
            {
                if (result != null)
                {
                    return null;
                }

                result = method;
            }
            else if (method.Name == ContractApiMetadata.OldMethodName &&
                     method.Parameters.Length == 1)
            {
                if (old != null)
                {
                    return null;
                }

                old = method;
            }
        }

        if (result == null || old == null)
        {
            return null;
        }

        return new ContractApiSymbols(
            clauses,
            result,
            old,
            selections);
    }

    internal BoundContractKind? GetClauseKind(IMethodSymbol method)
    {
        return Clauses.GetClauseKind(method);
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

        return ContractApiClauseProjection.GetClauseRole(definition.Name) switch
        {
            ContractApiClauseRole.Requires => BoundContractKind.Requires,
            ContractApiClauseRole.Ensures => BoundContractKind.Ensures,
            ContractApiClauseRole.Assume => BoundContractKind.Assume,
            _ => null
        };
    }
}
