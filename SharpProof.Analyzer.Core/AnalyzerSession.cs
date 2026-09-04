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
    private readonly Lazy<EffectAnalysisSession> _effects;
    private readonly Lazy<ContractSelectionInventory> _attributes;
    private readonly Lazy<ContractClauseInventoryBuilder> _contractClauses;
    private readonly Lazy<EffectiveContractSourceResolver> _contractSources;
    private readonly Lazy<ContractBinder> _contractBinder;
    private readonly Lazy<ContractIntrinsicValidator> _contractIntrinsics;
    private readonly Lazy<ResolvedApiSpecTable> _apiSpecs;
    private readonly Lazy<ConservativeEffectCallPreconditionPolicy>
        _callPreconditions;
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
        _requiresCallSiteAnalyses =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _executableAnalyses =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _selectedSemicolonAccessors =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, AnalyzerSemanticOutcome>
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
        _callPreconditions = CreateLazy(
            () => new ConservativeEffectCallPreconditionPolicy(
                compilation,
                cancellationToken: cancellationToken));
        _effects = CreateLazy(
            () => new EffectAnalysisSession(
                compilation,
                GetValue(_apiSpecs),
                new AnalyzerEffectCallPreconditionPolicy(
                    GetValue(_contractBinder),
                    GetValue(_contractClauses),
                    IrFactory,
                    new ConservativeEffectCallPreconditionPolicy(
                        compilation,
                        includeSourceCompanions: false,
                        cancellationToken: cancellationToken),
                    cancellationToken)));
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
    internal ResolvedApiSpecTable? EffectApiSpecs =>
        Configuration.EffectsEnabled ? GetValue(_effects).ApiSpecs : null;
    internal bool HasCreatedApiSpecs => _apiSpecs.IsValueCreated;
    internal bool HasCreatedEffectAnalysis => _effects.IsValueCreated;

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

    internal bool HasPotentialCallPreconditions(
        IMethodSymbol method)
    {
        method = EffectAnalysisSession.NormalizeMethod(method);
        if (method is
        { ContainingType: { StaticConstructors.Length: > 0 } } and
            ({ IsStatic: true } or { MethodKind: MethodKind.Constructor }))
        {
            return true;
        }

        if (GetValue(_callPreconditions).HasPotentialPreconditions(method) ||
            ResolveEffectContract(method) is
            { Kind: > EffectContractResolutionKind.Missing and < EffectContractResolutionKind.Valid })
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
        method = EffectAnalysisSession.NormalizeMethod(method);
        return method.DeclaringSyntaxReferences.IsEmpty &&
            method.Parameters.Any(parameter =>
                parameter.GetAttributes().Any(attribute =>
                    Attributes.IsRejectedClosedContract(attribute)));
    }

    internal bool TryBeginRequiresCallSiteAnalysis(
        IMethodSymbol method)
    {
        return _requiresCallSiteAnalyses.TryAdd(
            ContractClauseInventoryBuilder.NormalizeCallable(
                method),
            0);
    }

    internal bool TryBeginExecutableAnalysis(IMethodSymbol method)
    {
        return _executableAnalyses.TryAdd(
            EffectAnalysisSession.NormalizeMethod(method),
            0);
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetContractIntrinsicViolations(
        ContractClauseInventory inventory)
    {
        return GetValue(_contractIntrinsics).Validate(
            inventory.Callable,
            inventory.ImplementationBody,
            includeNestedCallables: true);
    }

    internal EffectContractResolution ResolveEffectContract(IMethodSymbol method)
    {
        return GetValue(_effects).ResolveExternalContract(method);
    }

    internal EffectMethodResult AnalyzeEffects(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cancellationToken.ThrowIfCancellationRequested();
        if (!Configuration.EffectsEnabled)
        {
            throw new InvalidOperationException(
                "Effect analysis was not enabled for this compilation.");
        }

        return GetValue(_effects).Analyze(method, cancellationToken);
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
        method = EffectAnalysisSession.NormalizeMethod(method);
        _semanticOutcomes.AddOrUpdate(
            method,
            outcome,
            (_, current) => AnalyzerSemanticOutcomes.Combine(current, outcome));
        _outcomeObserver?.Invoke(method, outcome);
    }

    internal void RegisterSelectedSemicolonAccessor(IMethodSymbol method)
    {
        _selectedSemicolonAccessors.TryAdd(
            EffectAnalysisSession.NormalizeMethod(method),
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
