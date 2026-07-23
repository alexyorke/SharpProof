namespace SharpProof.Analyzer;
internal sealed class AnalyzerSession : IDisposable {
    private readonly ConcurrentDictionary<IMethodSymbol, Lazy<MethodBodyAnalysisState>> _methodBodyAnalyses =
        new(SymbolEq.Default);
    private readonly MethodEffectAnalysisSession _effectAnalysis;
    internal AnalyzerSession(Compilation compilation, AnalyzerOptions options, CancellationToken cancellationToken) {
        Configuration = AnalyzerConfiguration.FromOptions(options);
        SmtAnalysis = new SmtAnalysisService(Configuration.SmtOptions);
        var configuredEffects = new ConfiguredEffectContractResolver(options.AnalyzerConfigOptionsProvider.GlobalOptions);
        _effectAnalysis = new MethodEffectAnalysisSession(
            compilation,
            cancellationToken,
            configuredEffects.Resolve,
            SmtAnalysis);
    }
    internal AnalyzerConfiguration Configuration { get; }
    internal SmtAnalysisService SmtAnalysis { get; }
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
                    MethodAnalysisSnapshot.Create(methodSymbol, declaration, semanticModel, operationBlocks, cancellationToken),
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
        SmtAnalysis.Dispose();
        _methodBodyAnalyses.Clear();
    }
}
