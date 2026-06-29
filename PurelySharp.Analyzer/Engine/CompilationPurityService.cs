using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using PurelySharp.Analyzer.Engine.Analysis;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Analyzer.Engine
{
    internal sealed class CompilationPurityService : System.IDisposable
    {
        private readonly ConcurrentDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> _purityCache = new(SymbolEqualityComparer.Default);
        private readonly object _fixedPointLock = new();

        public CompilationPurityService(Compilation compilation)
            : this(compilation, SmtAnalysisOptions.Default)
        {
        }

        public CompilationPurityService(Compilation compilation, SmtAnalysisOptions smtOptions)
        {
            _compilation = compilation;
            SmtAnalysis = new SmtAnalysisService(smtOptions);
        }

        public SmtAnalysisService SmtAnalysis { get; }

        public void Dispose()
        {
            SmtAnalysis.Dispose();
        }

        public PurityAnalysisEngine.PurityAnalysisResult GetPurity(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol)
        {
            EnsureFixedPoint(enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol);

            return _purityCache.GetOrAdd(methodSymbol, m =>
            {
                if (_fixedPoint!.TryGetValue(m, out var solved))
                {
                    return solved;
                }
                var engine = new PurityAnalysisEngine(this);
                return engine.IsConsideredPure(m, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol);
            });
        }

        private void EnsureFixedPoint(
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol)
        {
            if (_fixedPoint != null)
            {
                return;
            }

            lock (_fixedPointLock)
            {
                if (_fixedPoint != null)
                {
                    return;
                }

                _callGraph ??= CallGraphBuilder.Build(_compilation);
                _fixedPoint = WorklistPuritySolver.Solve(
                    _callGraph,
                    _compilation,
                    enforcePureAttributeSymbol,
                    allowSynchronizationAttributeSymbol,
                    SmtAnalysis);
            }
        }

        private CallGraph? _callGraph;
        private readonly Compilation _compilation;
        private volatile System.Collections.Immutable.ImmutableDictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult>? _fixedPoint;
    }
}
