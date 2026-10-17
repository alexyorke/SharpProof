namespace SharpProof.Contracts;

internal sealed class ContractApiSymbols(
    ContractClauseSymbols clauses,
    IMethodSymbol result,
    IMethodSymbol old,
    ContractSelectionInventory selections)
{
    internal ContractClauseSymbols Clauses { get; } = (clauses ?? throw new System.ArgumentNullException(nameof(clauses)));
    internal IMethodSymbol Result { get; } = (result ?? throw new System.ArgumentNullException(nameof(result)));
    internal IMethodSymbol Old { get; } = (old ?? throw new System.ArgumentNullException(nameof(old)));
    internal ContractSelectionInventory Selections { get; } = (selections ?? throw new System.ArgumentNullException(nameof(selections)));

    internal static ContractApiSymbols? TryCreate(Compilation compilation)
    {
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
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
        ArgumentNullGuard.NotNull(method, nameof(method));
        return SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Result);
    }

    internal bool IsOld(IMethodSymbol method)
    {
        ArgumentNullGuard.NotNull(method, nameof(method));
        return SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Old);
    }

}

internal sealed class ContractClauseSymbols(INamedTypeSymbol contractType)
{
    private static readonly ImmutableDictionary<ContractApiClauseRole, ContractClauseClassification> Classifications =
        new Dictionary<ContractApiClauseRole, ContractClauseClassification>
        {
            [ContractApiClauseRole.Requires] = ContractClauseClassification.Requires,
            [ContractApiClauseRole.Ensures] = ContractClauseClassification.Ensures,
            [ContractApiClauseRole.Assume] = ContractClauseClassification.Assume
        }.ToImmutableDictionary();

    internal INamedTypeSymbol ContractType { get; } = (contractType ?? throw new System.ArgumentNullException(nameof(contractType)));

    internal static ContractClauseSymbols? TryCreate(Compilation compilation)
    {
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        return ContractApiIdentityResolver.ForCompilation(compilation).Contract
            is { } contract
            ? new(contract)
            : null;
    }

    internal BoundContractKind? GetClauseKind(IMethodSymbol method)
    {
        ArgumentNullGuard.NotNull(method, nameof(method));
        return GetClauseClassification(method)?.Kind;
    }

    internal ContractClauseClassification? GetClauseClassification(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
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

        return Classifications.TryGetValue(
            ContractApiClauseProjection.GetClauseRole(definition.Name),
            out var classification)
            ? classification
            : null;
    }
}

internal sealed class ContractClauseClassification
{
    internal static readonly ContractClauseClassification Requires = new(BoundContractKind.Requires, 0);
    internal static readonly ContractClauseClassification Ensures = new(BoundContractKind.Ensures, 1);
    internal static readonly ContractClauseClassification Assume = new(BoundContractKind.Assume, 2);

    private ContractClauseClassification(BoundContractKind kind, int ordinalIndex)
    {
        Kind = kind;
        OrdinalIndex = ordinalIndex;
    }

    internal BoundContractKind Kind { get; }
    internal int OrdinalIndex { get; }
}
