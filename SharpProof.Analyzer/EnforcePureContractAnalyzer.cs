namespace SharpProof.Analyzer;

internal static class EnforcePureContractAnalyzer {
    internal static void Analyze(MethodBodyAnalysisContext context, SharpProofAttributeIdentityPolicy attributePolicy) {
        var method = context.MethodSymbol;
        if (!attributePolicy.HasAttribute(method, "EnforcePureAttribute")) return;

        var effects = context.State.GetMethodEffects(context.CancellationToken);
        if (effects.Purity == SharpProofVerdict.Proven) return;

        var firstSite = effects.Sites.FirstOrDefault(site =>
            site.Effect != SharpProof.Attributes.SharpProofEffect.Allocates &&
            site.Effect != SharpProof.Attributes.SharpProofEffect.Throws);
        var location = firstSite == null
            ? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node)
            : Location.Create(context.Node.SyntaxTree, new TextSpan(firstSite.SpanStart, firstSite.SpanLength));
        context.ReportDiagnostic(Diagnostic.Create(AnalyzerDiagnosticCatalog.Get("PurityNotVerifiedRule"), location, method.Name));
    }
}
