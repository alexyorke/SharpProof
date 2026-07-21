namespace SharpProof.Analyzer;

internal sealed class AnalyzerSession : IDisposable {
    private readonly ConcurrentDictionary<IMethodSymbol, Lazy<MethodBodyAnalysisState>> _methodBodyAnalyses =
        new(SymbolEq.Default);

    private readonly Lazy<AnalyzerProofService> _proofService;
    private readonly MethodEffectAnalysisSession _effectAnalysis;

    internal AnalyzerSession(
        Compilation compilation,
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        AnalyzerFeatures requestedFeatures) {
        Features = requestedFeatures;
        Configuration = AnalyzerConfiguration.FromOptions(options);
        AttributePolicy = SharpProofAttributeIdentityPolicy.Create();

        _proofService = new Lazy<AnalyzerProofService>(
            () => new AnalyzerProofService(
                Configuration.SmtOptions,
                Configuration.AnalysisLimits),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var configuredEffects = new ConfiguredEffectContractResolver(
            options.AnalyzerConfigOptionsProvider.GlobalOptions);
        _effectAnalysis = new MethodEffectAnalysisSession(
            compilation,
            cancellationToken,
            configuredEffects.Resolve,
            ProofService.SmtAnalysis);

    }

    internal AnalyzerFeatures Features { get; }

    internal AnalyzerConfiguration Configuration { get; }

    internal SharpProofAttributeIdentityPolicy AttributePolicy { get; }

    internal AnalyzerProofService ProofService => _proofService.Value;

    internal MethodBodyAnalysisState GetOrCreateMethodBodyAnalysis(
        IMethodSymbol methodSymbol,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken) {
        var lazy = _methodBodyAnalyses.GetOrAdd(
            methodSymbol,
            _ => new Lazy<MethodBodyAnalysisState>(
                () => new MethodBodyAnalysisState(
                    MethodAnalysisSnapshot.Create(
                        methodSymbol,
                        declaration,
                        semanticModel,
                        operationBlocks,
                        cancellationToken),
                    _effectAnalysis),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try {
            return lazy.Value;
        }
        catch {
            if (_methodBodyAnalyses.TryGetValue(methodSymbol, out var current) &&
                ReferenceEquals(current, lazy))
                _methodBodyAnalyses.TryRemove(methodSymbol, out _);

            throw;
        }
    }

    public void Dispose() {
        if (_proofService.IsValueCreated) _proofService.Value.Dispose();
        _methodBodyAnalyses.Clear();
    }
}
