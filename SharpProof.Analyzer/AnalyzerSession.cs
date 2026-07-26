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
    private readonly EffectAnalysisSession? _effects;
    private readonly ContractClauseInventoryBuilder _contractClauses;
    private readonly ContractBinder _contractBinder;
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly Action<IMethodSymbol, AnalyzerSemanticOutcome>? _outcomeObserver;
    private readonly ConcurrentDictionary<
        (SyntaxTree Tree, TextSpan Span),
        byte> _validatedAttributes = new();

    internal AnalyzerSession(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken,
        Action<IMethodSymbol, AnalyzerSemanticOutcome>? outcomeObserver = null) {
        cancellationToken.ThrowIfCancellationRequested();
        Compilation = compilation ??
            throw new ArgumentNullException(nameof(compilation));
        Configuration = configuration ??
            throw new ArgumentNullException(nameof(configuration));
        _outcomeObserver = outcomeObserver;
        Attributes = new AnalyzerAttributeSymbols(compilation);
        _contractClauses = new ContractClauseInventoryBuilder(compilation);
        _contractBinder = new ContractBinder(
            compilation,
            IrFactory,
            _contractClauses);
        _apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        if (configuration.Mode is SharpProofMode.Effects or SharpProofMode.AllExperimental)
            _effects = new EffectAnalysisSession(compilation, _apiSpecs);
    }

    internal Compilation Compilation { get; }
    internal AnalyzerConfiguration Configuration { get; }
    internal AnalyzerAttributeSymbols Attributes { get; }
    internal IrFactory IrFactory { get; } = new();
    internal ResolvedApiSpecTable ApiSpecs => _apiSpecs;
    internal ResolvedApiSpecTable? EffectApiSpecs => _effects?.ApiSpecs;
    internal ContractClauseInventory GetContractClauses(IMethodSymbol method) =>
        _contractClauses.Create(method);
    internal ContractBindingResult BindRequires(IMethodSymbol method) =>
        _contractBinder.BindRequires(method);

    internal EffectMethodResult AnalyzeEffects(
        IMethodSymbol method,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return (_effects ??
                throw new InvalidOperationException(
                    "Effect analysis was not enabled for this compilation."))
            .Analyze(method, cancellationToken);
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
                   (reference.SyntaxTree, reference.Span),
                   0);
    }
}
