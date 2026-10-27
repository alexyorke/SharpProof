namespace SharpProof.Contracts;

internal sealed partial class ContractBinder
{
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols? _api;
    private readonly ContractIntrinsicValidator _intrinsics;
    private readonly ContractClauseInventoryBuilder _clauseInventory;
    private ImmutableDictionary<IMethodSymbol, ContractBindingResult> _bindings =
        ImmutableDictionary.Create<IMethodSymbol, ContractBindingResult>(SymbolEqualityComparer.IncludeNullability);
    private ImmutableDictionary<IMethodSymbol, ContractBindingResult> _requiresBindings =
        ImmutableDictionary.Create<IMethodSymbol, ContractBindingResult>(SymbolEqualityComparer.IncludeNullability);
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
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
    }

    internal static ContractBinder CreateWithContractSources(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder clauseInventory,
        EffectiveContractSourceResolver contractSources)
    {
        clauseInventory = RepositoryParameterGuard.NotNull(clauseInventory, nameof(clauseInventory));
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
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
        _clauseInventory = clauseInventory ??
            ContractClauseInventoryBuilder.ForCompilation(compilation);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        _contractSources = contractSources ??
            (clauseInventory == null
                ? EffectiveContractSourceResolver.ForCompilation(compilation, cancellation.Token)
                : new EffectiveContractSourceResolver(
                    compilation,
                    clauseInventory, cancellation.Token));
    }

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        return Bind(target, implementationBody, cancellation.Token);
    }

    public ContractBindingResult Bind(IMethodSymbol target, CancellationToken cancellationToken)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return Bind(target, null, cancellationToken);
    }

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody,
            requiresOnly: false,
            cache: ref _bindings,
            cancellationToken: cancellationToken);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        return BindRequires(target, implementationBody, cancellation.Token);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody,
            requiresOnly: true,
            cache: ref _requiresBindings,
            cancellationToken: cancellationToken);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody: null,
            requiresOnly: true,
            cache: ref _requiresBindings,
            cancellationToken: cancellationToken);
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        return GetClauseInventory(target, cancellation.Token);
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return _clauseInventory.Create(target, null, cancellationToken);
    }

    private ContractBindingResult BindCached(
        IMethodSymbol target,
        IOperation? implementationBody,
        bool requiresOnly,
        ref ImmutableDictionary<IMethodSymbol, ContractBindingResult> cache,
        CancellationToken cancellationToken)
    {
        cache = RepositoryParameterGuard.NotNull(cache, nameof(cache));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return implementationBody == null
            ? ImmutableInterlocked.GetOrAdd(ref cache,
                target,
                value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (BindCore(value, null, requiresOnly, cancellationToken)))
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
        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        var invocationFailure = ClassifyInvocations(
            inventory, usesCompanion, requiresOnly, directIntrinsicsValidated && !usesCompanion, clauses);
        if (invocationFailure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(invocationFailure);
        }

        var attributeFailure = ClassifyClosedAttributes(target, requiresOnly, clauses);
        if (attributeFailure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(attributeFailure);
        }

        return ContractBindingResult.Success(new BoundMethodContracts(
            target, source, clauses.ToImmutable(), usesCompanion));
    }

    private static bool HasRequiresPlacementErrors(
        ContractClauseInventory inventory)
    {
        inventory = RepositoryParameterGuard.NotNull(inventory, nameof(inventory));
        return inventory.Clauses.Any(static clause =>
            clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires &&
            !clause.IsValid &&
            clause.Placement != ContractClausePlacement.NestedCallable));
    }

    // Classifies the valid contract invocations without lowering their
    // conditions; the Total binder owns expression semantics.
    private ContractBindingFailure ClassifyInvocations(
        ContractClauseInventory inventory,
        bool usesCompanion,
        bool requiresOnly,
        bool intrinsicsAlreadyValidated,
        ImmutableArray<BoundContractClause>.Builder clauses)
    {
        inventory = RepositoryParameterGuard.NotNull(inventory, nameof(inventory));
        clauses = RepositoryParameterGuard.NotNull(clauses, nameof(clauses));
        var body = inventory.ImplementationBody;
        if (body == null)
        {
            return ContractBindingFailure.None;
        }
        var failure = intrinsicsAlreadyValidated
            ? ContractBindingFailure.None
            : ValidateIntrinsics(inventory.Callable, body, requiresOnly);
        if (failure != ContractBindingFailure.None)
        {
            return failure;
        }
        foreach (var occurrence in inventory.Clauses)
        {
            if (occurrence is null || !occurrence.IsValid || (requiresOnly && occurrence.Kind != BoundContractKind.Requires))
            {
                continue;
            }
            var invocation = occurrence.Invocation;
            if (invocation.Arguments.Length != 1)
            {
                return ContractBindingFailure.InvalidIntrinsicSignature;
            }
            var condition = invocation.Arguments[0].Value;
            if (condition.Type?.SpecialType != SpecialType.System_Boolean)
            {
                return ContractBindingFailure.NonBooleanCondition;
            }
            clauses.Add(new BoundContractClause(
                occurrence.Kind,
                usesCompanion ? BoundContractEvidence.Companion : BoundContractEvidence.CompilerBoundInvocation,
                ContractStructuralKey.Create(condition)));
        }
        return ContractBindingFailure.None;
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

    private ContractBindingFailure ClassifyClosedAttributes(
        IMethodSymbol target,
        bool requiresOnly,
        ImmutableArray<BoundContractClause>.Builder clauses)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        clauses = RepositoryParameterGuard.NotNull(clauses, nameof(clauses));
        foreach (var site in ClosedContractAttributeValidator.EnumerateValueSites(
                     target,
                     includeReturn: !requiresOnly))
        {
            var siteKey = site.IsReturn
                ? "return"
                : "p" + site.ParameterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var attribute in site.Attributes)
            {
                if (_api!.Selections.IsRejectedClosedContract(attribute))
                {
                    return ContractBindingFailure.InvalidClosedAttribute;
                }
                var validation = ClosedContractAttributeValidator.Validate(
                    attribute, site.Type, site.RefKind, _api.Selections, includeUnsigned64: false);
                if (!validation.IsRecognized)
                {
                    continue;
                }
                if (!validation.IsValid || (site.IsReturn && target.ReturnsVoid))
                {
                    return ContractBindingFailure.InvalidClosedAttribute;
                }
                clauses.Add(new BoundContractClause(
                    site.IsReturn ? BoundContractKind.Ensures : BoundContractKind.Requires,
                    BoundContractEvidence.ClosedAttribute,
                    "attribute:" + siteKey + ":" + AttributeKey(attribute)));
            }
        }
        return ContractBindingFailure.None;
    }

    private static string AttributeKey(AttributeData attribute)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return attribute.AttributeClass?.GetDocumentationCommentId() + "(" +
            string.Join(",", attribute.ConstructorArguments.Select(static argument =>
            {
                RepositoryParameterGuard.RequireInitialized(argument.IsNull && argument.Kind == TypedConstantKind.Error, nameof(argument), "Attribute arguments must be valid.");
                return argument.ToCSharpString();
            })) + ")";
    }
}
