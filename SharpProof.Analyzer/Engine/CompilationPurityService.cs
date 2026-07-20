namespace SharpProof.Analyzer.Engine;

internal sealed class CompilationPurityService : IDisposable
{
    private readonly Compilation _compilation;

    private readonly ConcurrentDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> _purityCache =
        new(SymbolEq.Default);

    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _semanticModelCache = new();

    public CompilationPurityService(Compilation compilation)
        : this(compilation, SmtAnalysisOptions.Default, RequiresContractHelpers.OfficialAttributePolicy)
    {
    }


    public CompilationPurityService(
        Compilation compilation,
        SmtAnalysisOptions smtOptions,
        SharpProofAttributeIdentityPolicy attributePolicy)
        : this(compilation, smtOptions, attributePolicy, SharpProofAnalysisBudget.Default)
    {
    }

    public CompilationPurityService(
        Compilation compilation,
        SmtAnalysisOptions smtOptions,
        SharpProofAttributeIdentityPolicy attributePolicy,
        SharpProofAnalysisBudget analysisLimits)
    {
        _compilation = compilation;
        AttributePolicy = attributePolicy ?? throw new ArgumentNullException(nameof(attributePolicy));
        AnalysisLimits = analysisLimits ?? throw new ArgumentNullException(nameof(analysisLimits));
        SmtAnalysis = new SmtAnalysisService(smtOptions);
    }

    public SharpProofAttributeIdentityPolicy AttributePolicy { get; }

    public SharpProofAnalysisBudget AnalysisLimits { get; }

    public SmtAnalysisService SmtAnalysis { get; }

    internal int CachedPurityCount => _purityCache.Count;

    internal int CachedSemanticModelCount => _semanticModelCache.Count;

    public void Dispose()
    {
        SmtAnalysis.Dispose();
    }

    public PurityAnalysisEngine.PurityAnalysisResult GetPurity(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _purityCache.GetOrAdd(methodSymbol, m =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var methodSemanticModel = GetSemanticModelForMethod(m) ?? semanticModel;
            var engine = new PurityAnalysisEngine(SmtAnalysis, AttributePolicy, GetSemanticModelForMethod);
            using var limits = SymbolicAnalysisLimitContext.Push(
                AnalysisLimits,
                m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken));
            return engine.IsConsideredPure(m, methodSemanticModel, enforcePureAttributeSymbol,
                allowSynchronizationAttributeSymbol, cancellationToken, _purityCache);
        });
    }

    private SemanticModel GetSemanticModel(SyntaxTree syntaxTree) =>
        _semanticModelCache.GetOrAdd(syntaxTree, tree => _compilation.GetSemanticModel(tree));

    private SemanticModel? GetSemanticModelForMethod(IMethodSymbol methodSymbol)
    {
        foreach (var syntaxReference in methodSymbol.OriginalDefinition.DeclaringSyntaxReferences)
        {
            var syntaxTree = syntaxReference.SyntaxTree;
            if (_compilation.ContainsSyntaxTree(syntaxTree)) return GetSemanticModel(syntaxTree);
        }

        return null;
    }
}
