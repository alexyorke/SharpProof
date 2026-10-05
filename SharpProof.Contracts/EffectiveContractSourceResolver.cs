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
            clause.Placement != ContractClausePlacement.NestedCallable);
}

internal sealed class EffectiveContractSourceResolver
{
    private static readonly ConditionalWeakTable<
        Compilation, EffectiveContractSourceResolver> Cache = new();
    private readonly ContractClauseInventoryBuilder _clauses;
    private readonly ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> _companions;
    private readonly IReadOnlyDictionary<INamedTypeSymbol,
        ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor>>
        _companionsByTarget;
    private readonly ConcurrentDictionary<
        IMethodSymbol, EffectiveContractSourceResolution> _cache =
        new(SymbolEqualityComparer.IncludeNullability);

    internal EffectiveContractSourceResolver(
        Compilation compilation,
        ContractClauseInventoryBuilder clauses,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _clauses = ArgumentNullGuard.NotNull(clauses, nameof(clauses));
        _companions = ContractForSymbolMatcher.DiscoverCompanions(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            cancellationToken);
        _companionsByTarget = ContractForSymbolMatcher
            .BuildCompanionTargetIndex(_companions);
    }

    internal ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> Companions =>
        _companions;

    internal static EffectiveContractSourceResolver ForCompilation(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Cache.GetValue(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            value => new(
                value,
                ContractClauseInventoryBuilder.ForCompilation(value),
                cancellationToken));
    }

    internal EffectiveContractSourceResolution Resolve(
        IMethodSymbol target,
        IOperation? implementationBody = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));

        target = ContractClauseInventoryBuilder.NormalizeCallable(target);
        return implementationBody == null
            ? _cache.GetOrAdd(
                target,
                value => ResolveUncached(value, cancellationToken))
            : ResolveCore(target, implementationBody, cancellationToken);
    }

    private EffectiveContractSourceResolution ResolveUncached(
        IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        return ResolveCore(target, null, cancellationToken);
    }

    private EffectiveContractSourceResolution ResolveCore(
        IMethodSymbol target,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var direct = _clauses.Create(
            target,
            implementationBody,
            cancellationToken,
            alreadyNormalized: true);
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
                _companions,
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
