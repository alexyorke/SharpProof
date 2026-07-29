namespace SharpProof.Analyzer;

internal interface IAnalyzerSessionFactory {
    AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken);
}

internal sealed class DefaultAnalyzerSessionFactory : IAnalyzerSessionFactory {
    internal static DefaultAnalyzerSessionFactory Instance { get; } = new();

    private DefaultAnalyzerSessionFactory() {
    }

    public AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken) =>
        new(compilation, configuration, cancellationToken);
}

internal sealed class AnalyzerSession {
    private readonly EffectAnalysisSession _effects;
    private readonly ContractClauseInventoryBuilder _contractClauses;
    private readonly ContractBinder _contractBinder;
    private readonly ContractIntrinsicValidator _contractIntrinsics;
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly Action<IMethodSymbol, AnalyzerSemanticOutcome>? _outcomeObserver;
    private readonly ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedAttributes = new();
    private readonly ConcurrentDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedContractIntrinsics = new();

    internal AnalyzerSession(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken,
        Action<IMethodSymbol, AnalyzerSemanticOutcome>? outcomeObserver = null) {
        cancellationToken.ThrowIfCancellationRequested();
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _outcomeObserver = outcomeObserver;
        Attributes = ContractSelectionInventory.ForCompilation(compilation);
        _contractClauses = ContractClauseInventoryBuilder.ForCompilation(compilation);
        _contractBinder = new ContractBinder(compilation, IrFactory, _contractClauses);
        _contractIntrinsics = new ContractIntrinsicValidator(compilation);
        _apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        _effects = new EffectAnalysisSession(compilation, _apiSpecs);
    }

    internal Compilation Compilation { get; }
    internal AnalyzerConfiguration Configuration { get; }
    internal ContractSelectionInventory Attributes { get; }
    internal IrFactory IrFactory { get; } = new();
    internal ResolvedApiSpecTable ApiSpecs => _apiSpecs;
    internal ResolvedApiSpecTable? EffectApiSpecs =>
        Configuration.EffectsEnabled ? _effects.ApiSpecs : null;

    internal ContractClauseInventory GetContractClauses(IMethodSymbol method) =>
        _contractClauses.Create(method);

    internal ContractBindingResult BindRequires(IMethodSymbol method) =>
        _contractBinder.BindRequires(method);

    internal ImmutableArray<ContractIntrinsicViolation> GetContractIntrinsicViolations(
        ContractClauseInventory inventory) =>
        _contractIntrinsics.Validate(
            inventory.Callable,
            inventory.ImplementationBody,
            includeNestedCallables: true);

    internal EffectContractResolution ResolveEffectContract(IMethodSymbol method) =>
        _effects.ResolveExternalContract(method);

    internal EffectMethodResult AnalyzeEffects(
        IMethodSymbol method,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Configuration.EffectsEnabled)
            throw new InvalidOperationException(
                "Effect analysis was not enabled for this compilation.");
        return _effects.Analyze(method, cancellationToken);
    }

    internal bool HasResolvedApiSpec(IMethodSymbol method) =>
        _apiSpecs.TryGet(method, out _);

    internal bool IsKnownPure(IMethodSymbol method) =>
        _apiSpecs.IsPureAndAllocationFree(method);

    internal void RecordSemanticOutcome(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome) =>
        _outcomeObserver?.Invoke(method, outcome);

    internal bool TryMarkAttributeValidated(AttributeData attribute) {
        var reference = attribute.ApplicationSyntaxReference;
        return reference == null ||
               _validatedAttributes.TryAdd(
                   (reference.SyntaxTree, reference.Span), 0);
    }

    internal bool TryMarkContractIntrinsicValidated(
        ContractIntrinsicViolation violation) =>
        _validatedContractIntrinsics.TryAdd(
            (violation.Invocation.Syntax.SyntaxTree,
             violation.Invocation.Syntax.Span),
            0);
}
