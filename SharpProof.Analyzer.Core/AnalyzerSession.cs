namespace SharpProof.Analyzer;

internal interface IAnalyzerSessionFactory
{
    AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken);
}

internal sealed class DefaultAnalyzerSessionFactory : IAnalyzerSessionFactory
{
    internal static DefaultAnalyzerSessionFactory Instance { get; } = new();

    private DefaultAnalyzerSessionFactory()
    {
    }

    public AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        configuration = RepositoryParameterGuard.NotNull(configuration, nameof(configuration));
        return new(compilation, configuration, cancellationToken);
    }
}

internal sealed class AnalyzerSession
{
    private readonly Lazy<ExternalEffectResolver> _effectContracts;
    private readonly Lazy<ContractSelectionInventory> _attributes;
    private readonly Lazy<ContractClauseInventoryBuilder> _contractClauses;
    private readonly Lazy<EffectiveContractSourceResolver> _contractSources;
    private readonly Lazy<ContractBinder> _contractBinder;
    private readonly Lazy<ContractIntrinsicValidator> _contractIntrinsics;
    private readonly Lazy<ResolvedApiSpecTable> _apiSpecs;
    private readonly CancellationToken _cancellationToken;
    private readonly Action<IMethodSymbol, AnalyzerSemanticOutcome>? _outcomeObserver;
    private ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedAttributes = ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>.Empty;
    private ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedContractIntrinsics = ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>.Empty;
    private ImmutableDictionary<IMethodSymbol, byte>
        _reportedRejectedContractApis =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
    private ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _reportedRejectedControlAttributes = ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>.Empty;
    private ImmutableDictionary<IMethodSymbol, byte>
        _executableAnalyses =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
    private ImmutableDictionary<IMethodSymbol, byte>
        _selectedSemicolonAccessors =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
    private ImmutableDictionary<IMethodSymbol, byte>
        _semanticOutcomes =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);

    internal AnalyzerSession(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken,
        Action<IMethodSymbol, AnalyzerSemanticOutcome>? outcomeObserver = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        Configuration = ArgumentNullGuard.NotNull(configuration, nameof(configuration));
        _cancellationToken = cancellationToken;
        ClausePlacement = new(compilation, cancellationToken);
        _outcomeObserver = outcomeObserver;
        _attributes = CreateLazy(
            () => ContractSelectionInventory.ForCompilation(compilation));
        _contractClauses = CreateLazy(
            () => ContractClauseInventoryBuilder.ForCompilation(compilation));
        _contractSources = CreateLazy(
            () => EffectiveContractSourceResolver.ForCompilation(
                compilation,
                cancellationToken));
        _contractIntrinsics = CreateLazy(
            () => new ContractIntrinsicValidator(compilation));
        _contractBinder = CreateLazy(
            () => ContractBinder.CreateWithContractSources(
                compilation,
                IrFactory,
                GetValue(_contractClauses),
                GetValue(_contractSources)));
        _apiSpecs = CreateLazy(
            () => new ApiSpecResolver(ApiSpecTable.Default).Resolve(
                compilation));
        _effectContracts = CreateLazy(
            () => new ExternalEffectResolver(compilation, GetValue(_apiSpecs)));
    }

    internal Compilation Compilation
    {
        get;
    }
    internal AnalyzerConfiguration Configuration
    {
        get;
    }
    internal ContractSelectionInventory Attributes =>
        GetValue(_attributes);
    internal ContractClausePlacementDiagnostics ClausePlacement { get; }
    internal IrFactory IrFactory { get; } = new();
    internal ResolvedApiSpecTable ApiSpecs => GetValue(_apiSpecs);
    internal bool HasCreatedApiSpecs => _apiSpecs.IsValueCreated;

    // One identity for a callable however it is referenced.
    internal static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        var normalized = method.ReducedFrom ?? method;
        return (normalized.PartialImplementationPart ?? normalized).OriginalDefinition;
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

    // Whether a call to the method may have to establish a precondition: a
    // Requires clause, a closed parameter contract, or one that cannot bind.
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

    internal bool HasRejectedMetadataPrecondition(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        method = NormalizeMethod(method);
        return method.DeclaringSyntaxReferences.IsEmpty &&
            method.Parameters.Any(parameter =>
                parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.GetAttributes().Any(attribute =>
                    attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (Attributes.IsRejectedClosedContract(attribute)))));
    }

    internal bool TryBeginExecutableAnalysis(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return ImmutableInterlocked.TryAdd(ref _executableAnalyses,
            NormalizeMethod(method),
            (byte)0);
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

    internal EffectContractResolution ResolveEffectContract(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return GetValue(_effectContracts).ResolveContract(method);
    }

    internal void RecordSemanticOutcome(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        method = NormalizeMethod(method);
        ImmutableInterlocked.TryAdd(ref _semanticOutcomes, method, (byte)0);
        _outcomeObserver?.Invoke(method, outcome);
    }

    internal void RegisterSelectedSemicolonAccessor(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        ImmutableInterlocked.TryAdd(ref _selectedSemicolonAccessors,
            NormalizeMethod(method),
            (byte)0);
    }

    internal ImmutableArray<IMethodSymbol> GetUnrecordedSelectedSemicolonAccessors()
    {
        return [.. _selectedSemicolonAccessors.Keys
            .Where(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (!_semanticOutcomes.ContainsKey(method)))
            .Select(static method =>
            {
                method = RepositoryParameterGuard.NotNull(method, nameof(method));
                var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
                return (
                    Method: method,
                    FilePath: reference?.SyntaxTree.FilePath,
                    SpanStart: reference?.Span.Start ?? int.MaxValue);
            })
            .OrderBy(static item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.SpanStart)
            .Select(static item => item.Method)];
    }

    internal bool TryMarkAttributeValidated(AttributeData attribute)
    {
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        var reference = attribute.ApplicationSyntaxReference;
        return reference == null ||
               TryMarkAttributeValidated(
                   reference.SyntaxTree,
                   reference.Span);
    }

    internal bool TryMarkAttributeValidated(
        SyntaxTree tree,
        TextSpan span)
    {
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        return ImmutableInterlocked.TryAdd(ref _validatedAttributes, (tree, span), (byte)0);
    }

    internal bool TryMarkContractIntrinsicValidated(
        ContractIntrinsicViolation violation)
    {
        return ImmutableInterlocked.TryAdd(ref _validatedContractIntrinsics,
            (violation.Operation.Syntax.SyntaxTree,
             violation.Operation.Syntax.Span),
            (byte)0);
    }

    internal bool TryMarkRejectedContractApiReported(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return ImmutableInterlocked.TryAdd(ref _reportedRejectedContractApis,
            ContractClauseInventoryBuilder.NormalizeCallable(method),
            (byte)0);
    }

    internal bool TryMarkRejectedControlAttributeReported(
        AttributeData attribute)
    {
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        var reference = attribute.ApplicationSyntaxReference;
        return reference == null ||
            ImmutableInterlocked.TryAdd(ref _reportedRejectedControlAttributes,
                (reference.SyntaxTree, reference.Span),
                (byte)0);
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
