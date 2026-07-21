namespace SharpProof.Analyzer;

internal static class MethodPurityAnalyzer {
    internal static void AnalyzeSymbolForPurity(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var method = context.MethodSymbol;
        if (!attributePolicy.HasAttribute(method, "EnforcePureAttribute")) return;

        var effects = context.State.GetMethodEffects(context.CancellationToken);
        if (effects.Purity == SharpProofVerdict.Proven) return;

        var firstSite = effects.Sites.FirstOrDefault(site =>
            site.Effect != SharpProof.Attributes.SharpProofEffect.Allocates &&
            site.Effect != SharpProof.Attributes.SharpProofEffect.Throws);
        var location = firstSite == null
            ? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node)
            : Location.Create(
                context.Node.SyntaxTree,
                new TextSpan(firstSite.SpanStart, firstSite.SpanLength));
        var outcome = effects.Purity == SharpProofVerdict.Unknown ? "unknown" : "violated";
        var reason = firstSite?.Reason ?? effects.UnknownReasons.FirstOrDefault()?.Message ?? "method_effects";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.ImpurityCategoryProperty, reason)
            .Add("sharpproof.effects.flags", effects.Effects.ToString())
            .Add("sharpproof.effects.capabilities", effects.Capabilities.ToString());
        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            method,
            context.Node.SyntaxTree,
            firstSite?.Operation ?? "MethodEffects",
            firstSite?.Symbol,
            $"effects:{firstSite?.SpanStart ?? context.Node.SpanStart}:{reason}",
            location,
            "[EnforcePure]",
            outcome,
            effects.Purity == SharpProofVerdict.Unknown ? "SP-EFFECT-UNKNOWN" : null);

        var diagnostic = Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("PurityNotVerifiedRule"),
            location,
            null,
            properties,
            new object[] { method.Name });
        AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
    }
}
