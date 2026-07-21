using SharpProof.Attributes;

namespace SharpProof.Analyzer;

internal static class MethodAllocationAnalyzer {
    internal static void AnalyzeSymbolForZeroAllocations(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var method = context.MethodSymbol;
        if (!MethodContractHierarchy.EnumerateSources(method, context.CancellationToken)
                .Any(source => attributePolicy.HasAttribute(source, "ZeroAllocationsAttribute")))
            return;

        var effects = context.State.GetMethodEffects(context.CancellationToken);
        foreach (var site in effects.Sites.Where(static site =>
                     (site.Effect & SharpProofEffect.Allocates) != 0)) {
            var location = Location.Create(context.Node.SyntaxTree, new TextSpan(site.SpanStart, site.SpanLength));
            var properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
                ImmutableDictionary<string, string?>.Empty
                    .Add("sharpproof.allocation.kind", site.Reason)
                    .Add("sharpproof.allocation.symbol", site.Symbol),
                method,
                context.Node.SyntaxTree,
                site.Reason,
                null,
                DiagnosticEvidenceKey.ForSpanLength(site.Reason, site.SpanStart, site.SpanLength, site.Symbol),
                location,
                "[ZeroAllocations]",
                "violated",
                site.Reason);
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(context, baseline, Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("AllocationInZeroAllocationMethodRule"),
                location,
                null,
                properties,
                new object[] { site.Operation, method.Name }));
        }
    }

    internal static bool HasVisibleAllocationSites(MethodBodyAnalysisContext context) =>
        context.State.GetMethodEffects(context.CancellationToken).AllocationFree != SharpProofVerdict.Proven;
}
