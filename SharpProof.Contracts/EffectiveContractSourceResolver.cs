namespace SharpProof.Contracts;

internal sealed partial class EffectiveContractSourceResolution
{
    internal bool HasValidDirectClause =>
        DirectInventory.HasValidClause;
    internal bool HasSelectedContractIntent =>
        Failure is not (
            ContractBindingFailure.None or
            ContractBindingFailure.MissingCompanion) ||
        DirectInventory.HasRejectedContractApiUsage ||
        Inventory.HasRejectedContractApiUsage ||
        Inventory.Clauses.Any(static clause =>
            clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement != ContractClausePlacement.NestedCallable));
}

internal sealed class EffectiveContractSourceResolver
{
    private static readonly ConditionalWeakTable<
        Compilation, EffectiveContractSourceResolver> Cache = new();
    private readonly ContractClauseInventoryBuilder _clauses;
    private readonly ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> _companions;
    private readonly ImmutableDictionary<INamedTypeSymbol,
        ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor>>
        _companionsByTarget;
    private ImmutableDictionary<
        IMethodSymbol, EffectiveContractSourceResolution> _cache =
        ImmutableDictionary.Create<IMethodSymbol, EffectiveContractSourceResolution>(SymbolEqualityComparer.IncludeNullability);

    internal EffectiveContractSourceResolver(
        Compilation compilation,
        ContractClauseInventoryBuilder clauses,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _clauses = ArgumentNullGuard.NotNull(clauses, nameof(clauses));
        _companions = ContractForSymbolMatcher.DiscoverCompanions(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            cancellationToken: cancellationToken);
        _companionsByTarget = ContractForSymbolMatcher
            .BuildCompanionTargetIndex(_companions);
    }

    internal ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> Companions =>
        _companions;

    internal static EffectiveContractSourceResolver ForCompilation(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Cache.GetValue(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new(
                value,
                ContractClauseInventoryBuilder.ForCompilation(value),
                cancellationToken)));
    }

    internal EffectiveContractSourceResolution Resolve(
        IMethodSymbol target,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));

        target = ContractClauseInventoryBuilder.NormalizeCallable(target);
        return implementationBody == null
            ? ImmutableInterlocked.GetOrAdd(ref _cache,
                target,
                value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (ResolveUncached(value, cancellationToken)))
            : ResolveCore(target, implementationBody, cancellationToken);
    }

    private EffectiveContractSourceResolution ResolveUncached(
        IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return ResolveCore(target, null, cancellationToken);
    }

    private EffectiveContractSourceResolution ResolveCore(
        IMethodSymbol target,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        cancellationToken.ThrowIfCancellationRequested();
        var direct = _clauses.Create(
            target,
            implementationBody,
            alreadyNormalized: true,
            cancellationToken: cancellationToken);
        var directResolution = new EffectiveContractSourceResolution(
            target,
            direct,
            direct,
            usesCompanion: false,
            direct.HasPlacementErrors
                ? ContractBindingFailure.InvalidClausePlacement
                : ContractBindingFailure.None);
        if (direct.HasPlacementErrors || direct.HasValidClause)
        {
            return directResolution;
        }

        if (target.MethodKind == MethodKind.Ordinary)
        {
            var companion = ContractForSymbolMatcher.ResolveCompanion(
                target,
                _companionsByTarget);
            if (companion.Failure != ContractBindingFailure.None)
            {
                return new(
                    target,
                    direct,
                    direct,
                    usesCompanion: false,
                    companion.Failure);
            }

            if (companion.Method != null)
            {
                var inventory = _clauses.Create(
                    companion.Method,
                    implementationBody: null,
                    cancellationToken: cancellationToken,
                    alreadyNormalized: true);
                var failure = inventory.ImplementationBody == null
                    ? ContractBindingFailure.CompanionBodyUnavailable
                    : inventory.HasPlacementErrors
                        ? ContractBindingFailure.InvalidClausePlacement
                        : ContractBindingFailure.None;
                return new(
                    companion.Method,
                    direct,
                    inventory,
                    usesCompanion: true,
                    failure);
            }
        }

        return directResolution;
    }

}
