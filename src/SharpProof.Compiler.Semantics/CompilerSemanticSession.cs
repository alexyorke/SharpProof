namespace SharpProof.Compiler.Semantics;

// Compilation-bound facts shared by diagnostics and final-compilation capture.
// This session has no activation policy, diagnostic state, or advisory execution.
internal sealed class CompilerSemanticSession
{
    private readonly CancellationToken _cancellationToken;
    private readonly Lazy<ContractSelectionInventory> _attributes;
    private readonly Lazy<ResolvedApiSpecTable> _apiSpecs;
    private readonly Lazy<ExternalEffectResolver> _effectContracts;
    private readonly Lazy<ContractBinder> _contractBinder;
    private readonly Lazy<ContractClauseInventoryBuilder> _contractClauses;
    private readonly Lazy<EffectiveContractSourceResolver> _contractSources;
    private readonly Lazy<ContractIntrinsicValidator> _contractIntrinsics;

    internal CompilerSemanticSession(Compilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _cancellationToken = cancellationToken;
        _attributes = CreateLazy(() => ContractSelectionInventory.ForCompilation(compilation));
        _apiSpecs = CreateLazy(() => new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation));
        _effectContracts = CreateLazy(() => new ExternalEffectResolver(compilation, ApiSpecs));
        _contractClauses = CreateLazy(() => ContractClauseInventoryBuilder.ForCompilation(compilation));
        _contractSources = CreateLazy(() => EffectiveContractSourceResolver.ForCompilation(compilation, cancellationToken));
        _contractIntrinsics = CreateLazy(() => new ContractIntrinsicValidator(compilation));
        _contractBinder = CreateLazy(() => ContractBinder.CreateWithContractSources(compilation, IrFactory,
            GetValue(_contractClauses), GetValue(_contractSources)));
    }

    internal Compilation Compilation { get; }
    internal IrFactory IrFactory { get; } = new();
    internal ContractSelectionInventory Attributes => GetValue(_attributes);
    internal ResolvedApiSpecTable ApiSpecs => GetValue(_apiSpecs);
    internal bool HasCreatedApiSpecs => _apiSpecs.IsValueCreated;

    internal static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        var normalized = method.ReducedFrom ?? method;
        return (normalized.PartialImplementationPart ?? normalized).OriginalDefinition;
    }

    internal EffectContractResolution ResolveEffectContract(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return GetValue(_effectContracts).ResolveContract(method);
    }

    internal bool HasPotentialCallPreconditions(
        IMethodSymbol method)
    {
        method = NormalizeMethod(method);
        if (method.Parameters.Any(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.GetAttributes().Any(attribute =>
                attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (Attributes.IsClosedContract(attribute) || Attributes.IsRejectedClosedContract(attribute))))))
        {
            return true;
        }

        var binding = BindRequires(method);
        return !binding.IsSuccess ||
            binding.Contracts is not { } contracts ||
            contracts.Clauses.Any(static clause =>
                clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires));
    }

    internal ContractClauseInventory GetContractClauses(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return GetValue(_contractClauses).Create(
            method,
            implementationBody: null,
            cancellationToken: _cancellationToken);
    }

    internal EffectiveContractSourceResolution ResolveContractSource(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return GetValue(_contractSources).Resolve(
            method,
            implementationBody: null,
            cancellationToken: _cancellationToken);
    }

    internal bool IsContractCompanion(IMethodSymbol method)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        return ContractForSymbolMatcher.IsCompanionType(
            GetValue(_contractSources).Companions,
            method.ContainingType);
    }

    internal ContractBindingResult BindRequires(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return GetValue(_contractBinder).BindRequires(
            method,
            _cancellationToken);
    }

    internal bool HasRejectedMetadataPrecondition(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        method = NormalizeMethod(method);
        return method.DeclaringSyntaxReferences.IsEmpty &&
            method.Parameters.Any(parameter =>
                parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.GetAttributes().Any(attribute =>
                    attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (Attributes.IsRejectedClosedContract(attribute)))));
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetContractIntrinsicViolations(
        ContractClauseInventory inventory)
    {
        inventory = ArgumentNullGuard.NotNull(inventory, nameof(inventory));
        var body = inventory.ImplementationBody;
        if (inventory.Callable.MethodKind == MethodKind.Constructor)
        {
            for (var operation = body; operation != null; operation = operation.Parent)
            {
                if (operation is IConstructorBodyOperation &&
                    operation.Syntax is ConstructorDeclarationSyntax { Initializer: not null })
                {
                    body = operation;
                    break;
                }
            }
        }
        return GetContractIntrinsicViolations(
            inventory.Callable,
            body);
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetContractIntrinsicViolations(
        IMethodSymbol callable,
        IOperation? body)
    {
        return GetValue(_contractIntrinsics).Validate(
            callable,
            body,
            includeNestedCallables: true);
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetMemberInitializerContractIntrinsicViolations(
        IOperation initializer)
    {
        return GetValue(_contractIntrinsics).ValidateMemberInitializer(initializer);
    }

    private Lazy<T> CreateLazy<T>(Func<T> valueFactory)
    {
        valueFactory = RepositoryParameterGuard.NotNull(valueFactory, nameof(valueFactory));
        return new Lazy<T>(
            () =>
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var value = valueFactory();
                _cancellationToken.ThrowIfCancellationRequested();
                return value;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private T GetValue<T>(Lazy<T> value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        _cancellationToken.ThrowIfCancellationRequested();
        return value.Value;
    }
}
