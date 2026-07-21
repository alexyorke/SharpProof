namespace SharpProof.Analyzer;

internal sealed class AnalyzerSession : IDisposable {
    private readonly ConcurrentDictionary<IMethodSymbol, Lazy<MethodBodyAnalysisState>> _methodBodyAnalyses =
        new(SymbolEq.Default);

    private readonly ConcurrentDictionary<string, TrustedBoundaryReviewFinding> _trustedBoundaryFindings =
        new(StringComparer.Ordinal);

    private readonly Lazy<AnalyzerProofService> _proofService;
    private readonly MethodEffectAnalysisSession _effectAnalysis;

    internal AnalyzerSession(
        Compilation compilation,
        AnalyzerOptions options,
        CancellationToken cancellationToken,
        AnalyzerFeatures requestedFeatures) {
        Features = AnalyzerFeatureDependencies.Expand(requestedFeatures);
        Configuration = AnalyzerConfiguration.FromOptions(options);
        AttributePolicy = SharpProofAttributeIdentityPolicy.Create(Configuration.AttributeStubNamespaces);
        Baseline = DiagnosticBaseline.FromOptions(
            options,
            cancellationToken);

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
            configuredEffects.Resolve);

    }

    internal AnalyzerFeatures Features { get; }

    internal AnalyzerConfiguration Configuration { get; }

    internal SharpProofAttributeIdentityPolicy AttributePolicy { get; }

    internal DiagnosticBaseline Baseline { get; }

    internal AnalyzerProofService ProofService => _proofService.Value;

    internal int MethodBodyAnalysisCount => _methodBodyAnalyses.Count;

    internal void RecordTrustedBoundaryFinding(TrustedBoundaryReviewFinding finding) {
        _trustedBoundaryFindings.AddOrUpdate(
            finding.Key,
            finding,
            (_, existing) => CompareFindingLocation(finding, existing) < 0 ? finding : existing);
    }

    internal ImmutableArray<TrustedBoundaryReviewFinding> GetTrustedBoundaryFindings() {
        return _trustedBoundaryFindings.Values
            .OrderBy(static finding => finding.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Location.SourceSpan.Start)
            .ThenBy(static finding => finding.SymbolDisplay, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Source, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Value, StringComparer.Ordinal)
            .ToImmutableArray();
    }

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
        _trustedBoundaryFindings.Clear();
    }

    private static int CompareFindingLocation(
        TrustedBoundaryReviewFinding left,
        TrustedBoundaryReviewFinding right) {
        var pathComparison = string.CompareOrdinal(
            left.Location.SourceTree?.FilePath ?? string.Empty,
            right.Location.SourceTree?.FilePath ?? string.Empty);
        if (pathComparison != 0) return pathComparison;

        var startComparison = left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
        return startComparison != 0
            ? startComparison
            : left.Location.SourceSpan.Length.CompareTo(right.Location.SourceSpan.Length);
    }
}
