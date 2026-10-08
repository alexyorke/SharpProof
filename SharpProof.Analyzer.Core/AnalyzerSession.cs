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
    private readonly ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedAttributes = new();
    private readonly ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedContractIntrinsics = new();
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _reportedRejectedContractApis =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _reportedRejectedControlAttributes = new();
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _executableAnalyses =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _anonymousRequiresPlacementAnalyses =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _selectedSemicolonAccessors =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _semanticOutcomes =
            new(SymbolEqualityComparer.Default);

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
    internal IrFactory IrFactory { get; } = new();
    internal ResolvedApiSpecTable ApiSpecs => GetValue(_apiSpecs);
    internal bool HasCreatedApiSpecs => _apiSpecs.IsValueCreated;

    // One identity for a callable however it is referenced.
    internal static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        var normalized = method.ReducedFrom ?? method;
        return (normalized.PartialImplementationPart ?? normalized).OriginalDefinition;
    }

    internal ContractClauseInventory GetContractClauses(IMethodSymbol method)
    {
        return GetValue(_contractClauses).Create(
            method,
            implementationBody: null,
            cancellationToken: _cancellationToken);
    }

    internal EffectiveContractSourceResolution ResolveContractSource(
        IMethodSymbol method)
    {
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
        if (method.Parameters.Any(parameter => parameter.GetAttributes().Any(attribute =>
                Attributes.IsClosedContract(attribute) || Attributes.IsRejectedClosedContract(attribute))))
        {
            return true;
        }

        var binding = BindRequires(method);
        return !binding.IsSuccess ||
            binding.Contracts is not { } contracts ||
            contracts.Clauses.Any(static clause =>
                clause.Kind == BoundContractKind.Requires);
    }

    internal bool HasRejectedMetadataPrecondition(IMethodSymbol method)
    {
        method = NormalizeMethod(method);
        return method.DeclaringSyntaxReferences.IsEmpty &&
            method.Parameters.Any(parameter =>
                parameter.GetAttributes().Any(attribute =>
                    Attributes.IsRejectedClosedContract(attribute)));
    }

    internal bool TryBeginExecutableAnalysis(IMethodSymbol method)
    {
        return _executableAnalyses.TryAdd(
            NormalizeMethod(method),
            0);
    }

    internal bool TryBeginAnonymousRequiresPlacementAnalysis(
        IMethodSymbol method)
    {
        return _anonymousRequiresPlacementAnalyses.TryAdd(
            ContractClauseInventoryBuilder.NormalizeCallable(method),
            0);
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetContractIntrinsicViolations(
        ContractClauseInventory inventory)
    {
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
        return GetValue(_effectContracts).ResolveContract(method);
    }

    internal bool HasResolvedApiSpec(IMethodSymbol method)
    {
        return GetValue(_apiSpecs).TryGet(method, out _);
    }

    internal bool IsKnownPure(IMethodSymbol method)
    {
        return GetValue(_apiSpecs).IsPureAndAllocationFree(method);
    }

    internal void RecordSemanticOutcome(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome)
    {
        method = NormalizeMethod(method);
        _semanticOutcomes.TryAdd(method, 0);
        _outcomeObserver?.Invoke(method, outcome);
    }

    internal void RegisterSelectedSemicolonAccessor(IMethodSymbol method)
    {
        _selectedSemicolonAccessors.TryAdd(
            NormalizeMethod(method),
            0);
    }

    internal ImmutableArray<IMethodSymbol> GetUnrecordedSelectedSemicolonAccessors()
    {
        return [.. _selectedSemicolonAccessors.Keys
            .Where(method => !_semanticOutcomes.ContainsKey(method))
            .Select(static method =>
            {
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
        return _validatedAttributes.TryAdd((tree, span), 0);
    }

    internal bool TryMarkContractIntrinsicValidated(
        ContractIntrinsicViolation violation)
    {
        return _validatedContractIntrinsics.TryAdd(
            (violation.Operation.Syntax.SyntaxTree,
             violation.Operation.Syntax.Span),
            0);
    }

    internal bool TryMarkRejectedContractApiReported(
        IMethodSymbol method)
    {
        return _reportedRejectedContractApis.TryAdd(
            ContractClauseInventoryBuilder.NormalizeCallable(method),
            0);
    }

    internal bool TryMarkRejectedControlAttributeReported(
        AttributeData attribute)
    {
        var reference = attribute.ApplicationSyntaxReference;
        return reference == null ||
            _reportedRejectedControlAttributes.TryAdd(
                (reference.SyntaxTree, reference.Span),
                0);
    }

    private Lazy<T> CreateLazy<T>(Func<T> valueFactory)
    {
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
        _cancellationToken.ThrowIfCancellationRequested();
        return value.Value;
    }
}
