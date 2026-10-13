namespace SharpProof.Contracts;

public sealed partial class ContractBinder
{
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols? _api;
    private readonly ContractIntrinsicValidator _intrinsics;
    private readonly ContractCanonicalization _canonicalization;
    private readonly ContractClauseInventoryBuilder _clauseInventory;
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _bindings =
        new(SymbolEqualityComparer.IncludeNullability);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _requiresBindings =
        new(SymbolEqualityComparer.IncludeNullability);
    private readonly EffectiveContractSourceResolver _contractSources;

    public ContractBinder(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder? clauseInventory = null)
        : this(
            compilation,
            factory,
            clauseInventory,
            contractSources: null)
    {
    }

    internal static ContractBinder CreateWithContractSources(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder clauseInventory,
        EffectiveContractSourceResolver contractSources)
    {
        return new ContractBinder(
            compilation,
            factory,
            clauseInventory,
            ArgumentNullGuard.NotNull(
                contractSources,
                nameof(contractSources)));
    }

    private ContractBinder(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder? clauseInventory,
        EffectiveContractSourceResolver? contractSources)
    {
        compilation = ArgumentNullGuard.NotNull(
            compilation,
            nameof(compilation));
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _api = ContractApiSymbols.TryCreate(compilation);
        _intrinsics = new ContractIntrinsicValidator(_api);
        _canonicalization = new ContractCanonicalization(
            compilation,
            _factory);
        _clauseInventory = clauseInventory ??
            ContractClauseInventoryBuilder.ForCompilation(compilation);
        _contractSources = contractSources ??
            (clauseInventory == null
                ? EffectiveContractSourceResolver.ForCompilation(compilation)
                : new EffectiveContractSourceResolver(
                    compilation,
                    clauseInventory));
    }

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody,
            requiresOnly: false,
            cache: _bindings,
            cancellationToken: CancellationToken.None);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody,
            requiresOnly: true,
            cache: _requiresBindings,
            cancellationToken: CancellationToken.None);
    }

    internal ContractBindingResult BindRequires(
        IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody: null,
            requiresOnly: true,
            cache: _requiresBindings,
            cancellationToken: cancellationToken);
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return _clauseInventory.Create(target);
    }

    private ContractBindingResult BindCached(
        IMethodSymbol target,
        IOperation? implementationBody,
        bool requiresOnly,
        ConcurrentDictionary<IMethodSymbol, ContractBindingResult> cache,
        CancellationToken cancellationToken)
    {
        return implementationBody == null
            ? cache.GetOrAdd(
                target,
                value => BindCore(value, null, requiresOnly, cancellationToken))
            : BindCore(target, implementationBody, requiresOnly, cancellationToken);
    }

    private ContractBindingResult BindCore(
        IMethodSymbol target,
        IOperation? implementationBody,
        bool requiresOnly,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_api == null)
        {
            return ContractBindingResult.Fail(ContractBindingFailure.ContractApiUnavailable);
        }

        if (target.MethodKind is not (
                MethodKind.Ordinary or
                MethodKind.Constructor or
                MethodKind.StaticConstructor or
                MethodKind.PropertyGet or
                MethodKind.PropertySet or
                MethodKind.EventAdd or
                MethodKind.EventRemove or
                MethodKind.LocalFunction or
                MethodKind.ExplicitInterfaceImplementation or
                MethodKind.UserDefinedOperator or
                MethodKind.Conversion))
        {
            return ContractBindingResult.Fail(ContractBindingFailure.UnsupportedTarget);
        }

        var resolution = _contractSources.Resolve(
            target,
            implementationBody,
            cancellationToken);
        if (resolution.DirectInventory.HasRejectedContractApiUsage &&
            resolution.DirectInventory.ImplementationBody == null &&
            resolution.DirectInventory.Clauses.IsEmpty)
        {
            return ContractBindingResult.Fail(
                ContractBindingFailure.UnsupportedTarget);
        }
        var directIntrinsicsValidated = false;
        if (!resolution.HasValidDirectClause &&
            target.MethodKind == MethodKind.Ordinary)
        {
            var directFailure = ValidateIntrinsics(
                resolution.DirectInventory.Callable,
                resolution.DirectInventory.ImplementationBody,
                requiresOnly);
            if (directFailure != ContractBindingFailure.None)
            {
                return ContractBindingResult.Fail(directFailure);
            }

            directIntrinsicsValidated = true;
        }
        if (resolution.Failure != ContractBindingFailure.None &&
            (!requiresOnly ||
             resolution.Failure != ContractBindingFailure.InvalidClausePlacement ||
             HasRequiresPlacementErrors(resolution.Inventory)))
        {
            return ContractBindingResult.Fail(resolution.Failure);
        }

        var source = resolution.Source;
        var inventory = resolution.Inventory;
        var usesCompanion = resolution.UsesCompanion;
        var expressionBinder = new ContractExpressionBinder(
            _factory,
            _api,
            source,
            _canonicalization.CreateTypeSpecializer(source));
        var invocationResult = BindInvocations(
            expressionBinder,
            inventory,
            usesCompanion,
            requiresOnly,
            directIntrinsicsValidated && !usesCompanion);
        if (invocationResult.Failure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(invocationResult.Failure);
        }

        var canonical = _canonicalization.CreateVariables(
            target,
            includeResult: !requiresOnly);
        var substitutions = _canonicalization.CreateSubstitutions(
            source,
            usesCompanion,
            expressionBinder,
            canonical);
        if (substitutions == null)
        {
            return ContractBindingResult.Fail(ContractBindingFailure.UnsupportedExpression);
        }

        var substituted = ContractClauseSubstitution.Substitute(
            _factory, invocationResult.Clauses, substitutions);
        if (substituted.Failure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(substituted.Failure);
        }
        var clauses = substituted.Clauses.ToBuilder();

        var attributeFailure = BindClosedAttributes(
            target, canonical, requiresOnly, clauses);
        if (attributeFailure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(attributeFailure);
        }

        return ContractBindingResult.Success(new BoundMethodContracts(
            target, source, clauses.ToImmutable(), canonical.ToBoundVariables(), usesCompanion));
    }

    private static bool HasRequiresPlacementErrors(
        ContractClauseInventory inventory)
    {
        return inventory.Clauses.Any(static clause =>
            clause.Kind == BoundContractKind.Requires &&
            !clause.IsValid &&
            clause.Placement != ContractClausePlacement.NestedCallable);
    }

    private ContractInvocationMaterializationResult BindInvocations(
        ContractExpressionBinder expressionBinder,
        ContractClauseInventory inventory,
        bool usesCompanion,
        bool requiresOnly,
        bool intrinsicsAlreadyValidated)
    {
        var body = inventory.ImplementationBody;
        if (body == null)
        {
            return ContractInvocationMaterializationResult.Empty;
        }
        var failure = intrinsicsAlreadyValidated
            ? ContractBindingFailure.None
            : ValidateIntrinsics(inventory.Callable, body, requiresOnly);
        if (failure != ContractBindingFailure.None)
        {
            return new([], failure);
        }
        return ContractInvocationMaterialization.Bind(
            _factory, expressionBinder,
            [.. inventory.Clauses.Where(static occurrence => occurrence.IsValid)
                .Select(static occurrence => new ContractInvocationCandidate(occurrence.Kind, occurrence.Invocation))],
            usesCompanion, requiresOnly);
    }

    private ContractBindingFailure ValidateIntrinsics(
        IMethodSymbol source,
        IOperation? body,
        bool requiresOnly)
    {
        foreach (var violation in _intrinsics.Validate(source, body))
        {
            if (!requiresOnly || violation.EnclosingClauseKind == BoundContractKind.Requires)
            {
                return violation.Failure;
            }
        }

        return ContractBindingFailure.None;
    }

    private ContractBindingFailure BindClosedAttributes(
        IMethodSymbol target,
        ContractCanonicalVariables variables,
        bool requiresOnly,
        ImmutableArray<BoundContractClause>.Builder clauses)
    {
        foreach (var site in ClosedContractAttributeValidator.EnumerateValueSites(
                     target,
                     includeReturn: !requiresOnly))
        {
            var value = site.IsReturn
                ? variables.Result.HasValue
                    ? _factory.Variable(variables.Result.Value)
                    : null
                : _factory.Variable(variables.Parameters[site.ParameterIndex]);
            var result = BindValueAttributes(
                site.Attributes,
                site.Type,
                site.RefKind,
                value,
                site.IsReturn
                    ? "return value"
                    : target.Parameters[site.ParameterIndex].Name,
                site.IsReturn
                    ? BoundContractKind.Ensures
                    : BoundContractKind.Requires,
                clauses);
            if (result != ContractBindingFailure.None)
            {
                return result;
            }
        }
        return ContractBindingFailure.None;
    }

    private ContractBindingFailure BindValueAttributes(
        ImmutableArray<AttributeData> attributes,
        ITypeSymbol sourceType,
        RefKind refKind,
        IrTerm? value,
        string valueName,
        BoundContractKind kind,
        ImmutableArray<BoundContractClause>.Builder clauses)
    {
        var bound = ClosedContractAttributeBinding.BindValue(
            _factory, _api!.Selections, attributes, sourceType, refKind, () => value, valueName, kind,
            total: false, _ => _factory.CreateOperation("closed-attribute"));
        if (bound.Failure != ContractBindingFailure.None)
        {
            return bound.Failure;
        }
        foreach (var clause in bound.Clauses)
        {
            clauses.Add(new BoundContractClause(clause.Kind, clause.Condition, clause.SourceOperation,
                BoundContractEvidence.ClosedAttribute, clause.DiagnosticText, clause.SourceSyntax));
        }
        return ContractBindingFailure.None;
    }
}
