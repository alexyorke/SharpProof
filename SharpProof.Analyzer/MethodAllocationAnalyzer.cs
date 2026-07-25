using SharpProof.Attributes;
namespace SharpProof.Analyzer;
internal static class MethodAllocationAnalyzer {
    internal static void AnalyzeSymbolForZeroAllocations(MethodBodyAnalysisContext context) {
        var method = context.MethodSymbol;
        if (!MethodContractHierarchy.EnumerateSources(method, context.CancellationToken)
                .Any(source => SharpProofAttributeIdentityPolicy.HasAttribute(source, "ZeroAllocationsAttribute")))
            return;
        if (method.IsAbstract) return;
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;
        var effects = context.State.GetMethodEffects(context.CancellationToken);
        if (effects.AllocationFree == SharpProofVerdict.Unknown) {
            var reason = effects.UnknownReasons.IsDefaultOrEmpty
                ? "effect analysis returned Unknown"
                : string.Join(", ", effects.UnknownReasons.Select(static value => value.Message).Distinct(StringComparer.Ordinal));
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("ZeroAllocationsNotVerifiedRule"),
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                method.Name,
                reason));
        }
        foreach (var site in effects.Sites.Where(static site => (site.Effect & SharpProofEffect.Allocates) != 0)) {
            var tree = site.SourceTree ?? context.Node.SyntaxTree;
            var location = site.SpanStart >= 0 && site.SpanLength >= 0 &&
                           site.SpanStart + site.SpanLength <= tree.GetText(context.CancellationToken).Length
                ? Location.Create(tree, new TextSpan(site.SpanStart, site.SpanLength))
                : AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("AllocationInZeroAllocationMethodRule"),
                location,
                site.Operation,
                method.Name));
        }
    }
}
