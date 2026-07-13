using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal PurityAnalysisResult IsConsideredPure(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<IMethodSymbol, PurityAnalysisResult>? initialPurityCache = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceNode = GetDeclaringSyntax(methodSymbol, cancellationToken);
        var limits = _purityService?.AnalysisLimits ?? SymbolicAnalysisLimitContext.Limits;
        using var limitScope = SymbolicAnalysisLimitContext.Push(limits, sourceNode);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var purityCache = new Dictionary<IMethodSymbol, PurityAnalysisResult>(SymbolEqualityComparer.Default);
        if (initialPurityCache != null)
            foreach (var entry in initialPurityCache)
                if (!SymbolEqualityComparer.Default.Equals(entry.Key, methodSymbol))
                    purityCache[entry.Key] = entry.Value;


        var result = DeterminePurityRecursiveInternal(
            methodSymbol,
            semanticModel,
            enforcePureAttributeSymbol,
            allowSynchronizationAttributeSymbol,
            visited,
            purityCache,
            _smtAnalysis,
            _attributePolicy,
            cancellationToken,
            _purityService
        );


        purityCache[methodSymbol] = result;

        return result.WithAnalysisTruncation(limitScope.Snapshot());
    }


    private static string GetPuritySource(PurityAnalysisResult result)
    {
        if (result.IsPure) return "Assumed/Analyzed Pure";
        if (result.ImpureSyntaxNode != null) return "Analyzed Impure";

        return "Unknown/Default Impure";
    }
}
