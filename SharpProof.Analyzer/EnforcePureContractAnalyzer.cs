namespace SharpProof.Analyzer;
internal static class EnforcePureContractAnalyzer {
    internal static void Analyze(MethodBodyAnalysisContext context) {
        var method = context.MethodSymbol;
        if (!MethodContractHierarchy.EnumerateSources(method, context.CancellationToken)
                .Any(source => SharpProofAttributeIdentityPolicy.HasAttribute(source, "EnforcePureAttribute")))
            return;
        if (method.IsAbstract) return;
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;
        var effects = context.State.GetMethodEffects(context.CancellationToken);
        if (effects.Purity == SharpProofVerdict.Proven) return;
        var firstSite = effects.Sites.FirstOrDefault(site =>
            (site.Effect & MethodEffects.ImpureEffects) != 0);
        var location = firstSite == null
            ? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node)
            : CreateSiteLocation(context, firstSite);
        context.ReportDiagnostic(Diagnostic.Create(AnalyzerDiagnosticCatalog.Get("PurityNotVerifiedRule"), location, method.Name));
    }
    private static Location CreateSiteLocation(MethodBodyAnalysisContext context, MethodEffectSite site) {
        var tree = site.SourceTree ?? context.Node.SyntaxTree;
        return site.SpanStart >= 0 && site.SpanLength >= 0 &&
               site.SpanStart + site.SpanLength <= tree.GetText(context.CancellationToken).Length
            ? Location.Create(tree, new TextSpan(site.SpanStart, site.SpanLength))
            : AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
    }
}
