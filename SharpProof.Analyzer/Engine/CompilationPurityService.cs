using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal sealed class CompilationPurityService : IDisposable
{
    private readonly Compilation _compilation;
    private readonly object _fixedPointLock = new();

    private readonly ConcurrentDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> _purityCache =
        new(SymbolEq.Default);

    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _semanticModelCache = new();

    private ImmutableDictionary<IMethodSymbol, ImmutableHashSet<IMethodSymbol>>? _callGraph;
    private volatile ImmutableDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>? _fixedPoint;

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
        EnsureFixedPoint(enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, cancellationToken);

        return _purityCache.GetOrAdd(methodSymbol, m =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_fixedPoint!.TryGetValue(m, out var solved)) return solved;
            var methodSemanticModel = GetSemanticModelForMethod(m) ?? semanticModel;
            var engine = new PurityAnalysisEngine(this);
            return engine.IsConsideredPure(m, methodSemanticModel, enforcePureAttributeSymbol,
                allowSynchronizationAttributeSymbol, cancellationToken);
        });
    }

    private void EnsureFixedPoint(
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        CancellationToken cancellationToken)
    {
        if (_fixedPoint != null) return;

        cancellationToken.ThrowIfCancellationRequested();
        lock (_fixedPointLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_fixedPoint != null) return;

            _callGraph ??= CallGraphBuilder.Build(_compilation, GetSemanticModel, cancellationToken);
            using (SymbolicAnalysisLimitContext.Push(AnalysisLimits))
                _fixedPoint = WorklistPuritySolver.Solve(
                    _callGraph,
                    _compilation,
                    enforcePureAttributeSymbol,
                    allowSynchronizationAttributeSymbol,
                    SmtAnalysis,
                    AttributePolicy,
                    GetSemanticModel,
                    cancellationToken);
        }
    }

    private SemanticModel GetSemanticModel(SyntaxTree syntaxTree)
    {
        return _semanticModelCache.GetOrAdd(syntaxTree, tree => _compilation.GetSemanticModel(tree));
    }

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
