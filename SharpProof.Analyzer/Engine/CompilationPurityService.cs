using System.Collections.Concurrent;
using System.Threading;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine
{
    internal sealed class CompilationPurityService : System.IDisposable
    {
        private readonly ConcurrentDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> _purityCache = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _semanticModelCache = new();
        private readonly object _fixedPointLock = new();

        public CompilationPurityService(Compilation compilation)
            : this(compilation, SmtAnalysisOptions.Default, RequiresContractHelpers.OfficialAttributePolicy)
        {
        }

        public CompilationPurityService(Compilation compilation, SmtAnalysisOptions smtOptions)
            : this(compilation, smtOptions, RequiresContractHelpers.OfficialAttributePolicy)
        {
        }

        public CompilationPurityService(
            Compilation compilation,
            SmtAnalysisOptions smtOptions,
            SharpProofAttributeIdentityPolicy attributePolicy)
        {
            _compilation = compilation;
            AttributePolicy = attributePolicy ?? throw new System.ArgumentNullException(nameof(attributePolicy));
            SmtAnalysis = new SmtAnalysisService(smtOptions);
        }

        public SharpProofAttributeIdentityPolicy AttributePolicy { get; }

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
                if (_fixedPoint!.TryGetValue(m, out var solved))
                {
                    return solved;
                }
                var engine = new PurityAnalysisEngine(this);
                return engine.IsConsideredPure(m, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, cancellationToken: cancellationToken);
            });
        }

        private void EnsureFixedPoint(
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            CancellationToken cancellationToken)
        {
            if (_fixedPoint != null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (_fixedPointLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_fixedPoint != null)
                {
                    return;
                }

                _callGraph ??= CallGraphBuilder.Build(_compilation, GetSemanticModel, cancellationToken);
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

        private System.Collections.Immutable.ImmutableDictionary<IMethodSymbol, System.Collections.Immutable.ImmutableHashSet<IMethodSymbol>>? _callGraph;
        private readonly Compilation _compilation;
        private volatile System.Collections.Immutable.ImmutableDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>? _fixedPoint;
    }
}
