namespace SharpProof.Analyzer;
internal static partial class ExceptionFlowAnalyzer {
    internal static void AnalyzeSymbolForExceptions(MethodBodyAnalysisContext context) {
        var contracts = CollectExceptionContracts(context);
        if (contracts.IsDefaultOrEmpty || context.MethodSymbol.IsAbstract) return;
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;
        var effects = context.State.GetMethodEffects(context.CancellationToken);
        var facts = ProjectEffectFacts(context, effects.ExceptionFacts)
            .ToImmutableArray();
        foreach (var contract in contracts)
            AnalyzeExceptionContract(context, context.MethodSymbol, contract, facts);
    }
    private static ImmutableArray<ExceptionFactView> ProjectEffectFacts(
        MethodBodyAnalysisContext context,
        ImmutableArray<MethodExceptionFact> facts) => [.. facts
        .Where(static fact => fact.Escape != SharpProofVerdict.Disproven)
        .Select(fact => ProjectEffectFact(context, fact))];
    private static ExceptionFactView ProjectEffectFact(
        MethodBodyAnalysisContext context,
        MethodExceptionFact fact) {
        var root = fact.SourceTree?.GetRoot(context.CancellationToken) ?? context.Node;
        var site = FindSite(root, fact.SpanStart, fact.SpanStart + fact.SpanLength);
        var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(site.SyntaxTree);
        return new ExceptionFactView(
            site,
            fact.ExceptionType,
            ResolveExceptionType(semanticModel, site, fact.ExceptionType),
            fact.Escape);
    }
    private static SyntaxNode FindSite(SyntaxNode method, int start, int end) =>
        method.DescendantNodesAndSelf().FirstOrDefault(node => node.SpanStart == start && node.Span.End == end) ??
        method.DescendantNodesAndSelf().Where(node => node.Span.Contains(start))
            .OrderBy(static node => node.Span.Length).FirstOrDefault() ?? method;
    private static ITypeSymbol? ResolveExceptionType(
        SemanticModel semanticModel,
        SyntaxNode site,
        string name) {
        var operation = semanticModel.GetOperation(site);
        var thrownType = operation switch {
            IThrowOperation { Exception.Type: { } exceptionType } => exceptionType,
            _ => null
        };
        if (thrownType != null) return thrownType;
        var normalizedName = name.Replace("global::", string.Empty);
        var direct = semanticModel.Compilation.GetTypeByMetadataName(normalizedName);
        if (direct != null) return direct;
        var simpleNameStart = normalizedName.LastIndexOf('.') + 1;
        var simpleName = normalizedName.Substring(simpleNameStart);
        return semanticModel.Compilation
            .GetSymbolsWithName(simpleName, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(candidate => string.Equals(
                candidate.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                normalizedName,
                StringComparison.Ordinal));
    }
    private static Location GetExceptionSiteLocation(SyntaxNode node) => node.GetLocation();
    private readonly record struct ExceptionFactView(SyntaxNode Site, string ExceptionType, ITypeSymbol? Type, SharpProofVerdict Escape);
}
