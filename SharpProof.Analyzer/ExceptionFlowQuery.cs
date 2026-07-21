namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine {
    internal static ExceptionFlowResult AnalyzeMethod(
        IMethodSymbol methodSymbol,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        HashSet<IMethodSymbol>? visitedMethods = null) {
        if (methodSymbol == null) throw new ArgumentNullException(nameof(methodSymbol));
        if (methodNode == null) throw new ArgumentNullException(nameof(methodNode));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
        visitedMethods ??= new HashSet<IMethodSymbol>(SymbolEq.Default) {
            methodSymbol.OriginalDefinition
        };
        var runtimeHazards = QueryRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis);
        return new ExceptionFlowResult(
            CollectUncaughtExceptionSiteEntries(
                methodNode,
                semanticModel,
                cancellationToken,
                methodSymbol,
                visitedMethods,
                smtAnalysis,
                attributePolicy,
                runtimeHazards)
            .ToImmutableArray(),
            runtimeHazards);
    }
}
