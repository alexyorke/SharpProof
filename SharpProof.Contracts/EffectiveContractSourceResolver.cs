namespace SharpProof.Contracts;

internal sealed partial class EffectiveContractSourceResolution
{
    internal bool HasValidDirectClause =>
        DirectInventory.Clauses.Any(static clause => clause.IsValid);
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
    private readonly ConcurrentDictionary<
        IMethodSymbol, EffectiveContractSourceResolution> _cache =
        new(SymbolEqualityComparer.IncludeNullability);

    internal EffectiveContractSourceResolver(
        Compilation compilation,
        ContractClauseInventoryBuilder clauses)
    {
        _clauses = ArgumentNullGuard.NotNull(clauses, nameof(clauses));
        _companions = ContractForSymbolMatcher.DiscoverCompanions(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)));
    }

    internal ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> Companions =>
        _companions;

    internal static EffectiveContractSourceResolver ForCompilation(
        Compilation compilation)
    {
        return Cache.GetValue(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            static value => new(
                value,
                ContractClauseInventoryBuilder.ForCompilation(value)));
    }

    internal EffectiveContractSourceResolution Resolve(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));

        target = ContractClauseInventoryBuilder.NormalizeCallable(target);
        return implementationBody == null
            ? _cache.GetOrAdd(target, ResolveUncached)
            : ResolveCore(target, implementationBody);
    }

    private EffectiveContractSourceResolution ResolveUncached(
        IMethodSymbol target)
    {
        return ResolveCore(target, null);
    }

    private EffectiveContractSourceResolution ResolveCore(
        IMethodSymbol target,
        IOperation? implementationBody)
    {
        var direct = _clauses.Create(target, implementationBody);
        if (direct.Clauses.Any(static clause => clause.IsValid))
        {
            return Create(
                target,
                direct,
                direct,
                usesCompanion: false,
                direct.HasPlacementErrors
                    ? ContractBindingFailure.InvalidClausePlacement
                    : ContractBindingFailure.None);
        }

        if (target.MethodKind == MethodKind.Ordinary)
        {
            var companion = ContractForSymbolMatcher.ResolveCompanion(
                _companions,
                target);
            if (companion.Failure != ContractBindingFailure.None)
            {
                return Create(
                    target,
                    direct,
                    direct,
                    usesCompanion: false,
                    companion.Failure);
            }

            if (companion.Method != null)
            {
                var inventory = _clauses.Create(companion.Method);
                var failure = inventory.ImplementationBody == null
                    ? ContractBindingFailure.CompanionBodyUnavailable
                    : inventory.HasPlacementErrors
                        ? ContractBindingFailure.InvalidClausePlacement
                        : ContractBindingFailure.None;
                return Create(
                    companion.Method,
                    direct,
                    inventory,
                    usesCompanion: true,
                    failure);
            }
        }

        return Create(
            target,
            direct,
            direct,
            usesCompanion: false,
            direct.HasPlacementErrors
                ? ContractBindingFailure.InvalidClausePlacement
                : ContractBindingFailure.None);
    }

    private static EffectiveContractSourceResolution Create(
        IMethodSymbol source,
        ContractClauseInventory directInventory,
        ContractClauseInventory inventory,
        bool usesCompanion,
        ContractBindingFailure failure)
    {
        return new EffectiveContractSourceResolution(
            source,
            directInventory,
            inventory,
            usesCompanion,
            failure);
    }
}
