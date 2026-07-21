namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer {
    internal static void AnalyzeSymbolForExceptions(
        MethodBodyAnalysisContext context,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var contracts = CollectExceptionContracts(context, attributePolicy);
        if (contracts.IsDefaultOrEmpty) return;

        var effects = context.State.GetMethodEffects(context.CancellationToken);
        var facts = ProjectEffectFacts(context, effects.ExceptionFacts)
            .Where(static fact => fact.Escape == SharpProofVerdict.Proven)
            .ToImmutableArray();
        AnalyzeExceptionContracts(context, context.MethodSymbol, contracts, facts);
    }

    private static ImmutableArray<ExceptionFactView> ProjectEffectFacts(
        MethodBodyAnalysisContext context,
        ImmutableArray<MethodExceptionFact> facts) => facts
        .Where(static fact => fact.Escape != SharpProofVerdict.Disproven)
        .Select(fact => new ExceptionFactView(
            FindSite(context.Node, fact.SpanStart, fact.SpanStart + fact.SpanLength),
            fact.ExceptionType,
            ResolveExceptionType(context.SemanticModel.Compilation, fact.ExceptionType),
            fact.Escape))
        .ToImmutableArray();

    private static SyntaxNode FindSite(SyntaxNode method, int start, int end) =>
        method.DescendantNodesAndSelf().FirstOrDefault(node => node.SpanStart == start && node.Span.End == end) ??
        method.DescendantNodesAndSelf().Where(node => node.Span.Contains(start))
            .OrderBy(static node => node.Span.Length).FirstOrDefault() ?? method;

    private static ITypeSymbol? ResolveExceptionType(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name.Replace("global::", string.Empty));

    private static Location GetExceptionSiteLocation(SyntaxNode node) => node.GetLocation();

    private readonly record struct ExceptionFactView(
        SyntaxNode Site,
        string ExceptionType,
        ITypeSymbol? Type,
        SharpProofVerdict Escape);
}
