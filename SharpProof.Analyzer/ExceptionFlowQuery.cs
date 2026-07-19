namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine
{
    internal static ExceptionFlowResult AnalyzeMethod(
        SymbolicMethodAnalysisInput input,
        CancellationToken cancellationToken,
        EffectSummaryCatalog exceptionSummaryCatalog,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        HashSet<IMethodSymbol>? visitedMethods = null)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        var methodNode = input.Declaration;
        var semanticModel = input.SemanticModel;
        var methodSymbol = input.MethodSymbol;
        var isRoot = visitedMethods == null;
        visitedMethods ??= new HashSet<IMethodSymbol>(SymbolEq.Default)
        {
            methodSymbol.OriginalDefinition
        };
        using var attributeScope = isRoot ? ExceptionFlowAnalyzer.UseAttributePolicy(attributePolicy) : null;
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
                exceptionSummaryCatalog,
                visitedMethods,
                smtAnalysis,
                attributePolicy,
                runtimeHazards)
            .ToImmutableArray(),
            runtimeHazards);
    }
}
