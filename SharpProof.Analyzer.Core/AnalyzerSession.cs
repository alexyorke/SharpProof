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
    private readonly Action<IMethodSymbol, AnalyzerSemanticOutcome>? _outcomeObserver;
    private readonly ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedAttributes = new();
    private readonly ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedContractIntrinsics = new();
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _reportedRejectedContractApis =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _requiresCallSiteAnalyses =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, byte>
        _executableAnalyses =
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
        _outcomeObserver = outcomeObserver;
        _attributes = new(
            () => ContractSelectionInventory.ForCompilation(compilation),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _contractClauses = new(
            () => ContractClauseInventoryBuilder.ForCompilation(compilation),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _contractSources = new(
            () => EffectiveContractSourceResolver.ForCompilation(compilation),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _contractIntrinsics = new(
            () => new ContractIntrinsicValidator(compilation),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _contractBinder = new(
            () => new ContractBinder(
                compilation,
                IrFactory,
                _contractClauses.Value),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _apiSpecs = new(
            () => new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _callPreconditions = new(
            () => new ConservativeEffectCallPreconditionPolicy(
                compilation),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _effects = new(
            () => new EffectAnalysisSession(
                compilation,
                _apiSpecs.Value,
                new AnalyzerEffectCallPreconditionPolicy(
                    _contractBinder.Value,
                    _contractClauses.Value,
                    new ConservativeEffectCallPreconditionPolicy(
                        compilation,
                        includeSourceCompanions: false))),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal Compilation Compilation
    {
        get;
    }
    internal AnalyzerConfiguration Configuration
    {
        get;
    }
    internal ContractSelectionInventory Attributes => _attributes.Value;
    internal IrFactory IrFactory { get; } = new();
    internal ResolvedApiSpecTable ApiSpecs => _apiSpecs.Value;
    internal ResolvedApiSpecTable? EffectApiSpecs =>
        Configuration.EffectsEnabled ? _effects.Value.ApiSpecs : null;
    internal bool HasCreatedApiSpecs => _apiSpecs.IsValueCreated;
    internal bool HasCreatedEffectAnalysis => _effects.IsValueCreated;

    internal ContractClauseInventory GetContractClauses(IMethodSymbol method)
    {
        return _contractClauses.Value.Create(method);
    }

    internal EffectiveContractSourceResolution ResolveContractSource(
        IMethodSymbol method)
    {
        return _contractSources.Value.Resolve(method);
    }

    internal ContractBindingResult BindRequires(IMethodSymbol method)
    {
        return _contractBinder.Value.BindRequires(method);
    }

    internal bool HasPotentialCallPreconditions(
        IMethodSymbol method)
    {
        method = EffectAnalysisSession.NormalizeMethod(method);
        if (_callPreconditions.Value.HasPotentialPreconditions(method) ||
            ResolveEffectContract(method) is
            { Kind: > EffectContractResolutionKind.Missing and < EffectContractResolutionKind.Valid })
        {
            return true;
        }

        if (method is
        { ContainingType: { StaticConstructors.Length: > 0 } } and
            ({ IsStatic: true } or { MethodKind: MethodKind.Constructor }))
        {
            return true;
        }

        var binding = BindRequires(method);
        return !binding.IsSuccess ||
            binding.Contracts is not { } contracts ||
            contracts.Clauses.Any(static clause =>
                clause.Kind == BoundContractKind.Requires);
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
        return _contractIntrinsics.Value.Validate(
            inventory.Callable,
            inventory.ImplementationBody,
            includeNestedCallables: true);
    }

    internal EffectContractResolution ResolveEffectContract(IMethodSymbol method)
    {
        return _effects.Value.ResolveExternalContract(method);
    }

    internal EffectMethodResult AnalyzeEffects(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Configuration.EffectsEnabled)
        {
            throw new InvalidOperationException(
                "Effect analysis was not enabled for this compilation.");
        }

        return _effects.Value.Analyze(method, cancellationToken);
    }

    internal bool HasResolvedApiSpec(IMethodSymbol method)
    {
        return _apiSpecs.Value.TryGet(method, out _);
    }

    internal bool IsKnownPure(IMethodSymbol method)
    {
        return _apiSpecs.Value.IsPureAndAllocationFree(method);
    }

    internal void RecordSemanticOutcome(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome)
    {
        _outcomeObserver?.Invoke(method, outcome);
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
            (violation.Invocation.Syntax.SyntaxTree,
             violation.Invocation.Syntax.Span),
            0);
    }

    internal bool TryMarkRejectedContractApiReported(
        IMethodSymbol method)
    {
        return _reportedRejectedContractApis.TryAdd(
            ContractClauseInventoryBuilder.NormalizeCallable(method),
            0);
    }
}
