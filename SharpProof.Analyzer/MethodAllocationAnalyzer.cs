using SharpProof.Attributes;

namespace SharpProof.Analyzer;

internal static class MethodAllocationAnalyzer {
    internal static void AnalyzeSymbolForZeroAllocations(
        MethodBodyAnalysisContext context,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var method = context.MethodSymbol;
        if (!MethodContractHierarchy.EnumerateSources(method, context.CancellationToken)
                .Any(source => attributePolicy.HasAttribute(source, "ZeroAllocationsAttribute")))
            return;

        var effects = context.State.GetMethodEffects(context.CancellationToken);
        foreach (var site in effects.Sites.Where(static site =>
                     (site.Effect & SharpProofEffect.Allocates) != 0)) {
            var location = Location.Create(context.Node.SyntaxTree, new TextSpan(site.SpanStart, site.SpanLength));
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("AllocationInZeroAllocationMethodRule"),
                location,
                site.Operation,
                method.Name));
        }
    }
}
