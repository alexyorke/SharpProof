using SharpProof.Attributes;
namespace SharpProof.Analyzer;
internal static class MethodCapabilityAnalyzer {
    internal static void AnalyzeSymbolForCapabilities(MethodBodyAnalysisContext context) {
        if (!TryGetAllowedCapabilities(context, out var allowed)) return;
        var method = context.MethodSymbol;
        if (method.IsAbstract) return;
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;
        var effects = context.State.GetMethodEffects(context.CancellationToken);
        var reportedCapabilities = SharpProofCapability.None;
        foreach (var site in effects.Sites) {
            var disallowed = site.Capabilities & ~allowed;
            if (disallowed == SharpProofCapability.None) continue;
            reportedCapabilities |= disallowed;
            var location = CreateSiteLocation(context, site);
            var text = disallowed.ToString();
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("CapabilityViolationRule"),
                location,
                site.Operation,
                method.Name,
                text));
        }
        var aggregateDisallowed = effects.Capabilities & ~allowed & ~reportedCapabilities;
        if (aggregateDisallowed != SharpProofCapability.None)
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("CapabilityViolationRule"),
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                "<method body>",
                method.Name,
                aggregateDisallowed.ToString()));
        if (effects.UnknownReasons.IsDefaultOrEmpty) return;
        var unknown = effects.UnknownReasons[0];
        var declarationLocation = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
        context.ReportDiagnostic(Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("CapabilityUnknownRule"),
            declarationLocation,
            "<method body>",
            method.Name,
            unknown.Message));
    }
    private static bool TryGetAllowedCapabilities(MethodBodyAnalysisContext context, out SharpProofCapability allowed) {
        allowed = SharpProofCapability.None;
        var found = false;
        foreach (var source in MethodContractHierarchy.EnumerateSources(context.MethodSymbol, context.CancellationToken))
            foreach (var attribute in SharpProofAttributeIdentityPolicy.GetAcceptedAttributes(
                         source, "AllowedCapabilitiesAttribute")) {
                if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value == null)
                    continue;
                var declared = (SharpProofCapability)Convert.ToInt32(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
                if (!EnumFlagsDefined(declared)) {
                    var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken);
                    context.ReportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                        "[AllowedCapabilities]",
                        syntax?.ToString() ?? declared.ToString(),
                        "contains undefined capability flags",
                        syntax?.GetLocation() ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node)));
                    continue;
                }
                allowed = found ? allowed & declared : declared;
                found = true;
            }
        return found;
    }
    private static bool EnumFlagsDefined(SharpProofCapability value) {
        var all = Enum.GetValues(typeof(SharpProofCapability))
            .Cast<SharpProofCapability>()
            .Aggregate(SharpProofCapability.None, static (current, item) => current | item);
        return (value & ~all) == 0;
    }
    private static Location CreateSiteLocation(MethodBodyAnalysisContext context, MethodEffectSite site) {
        var tree = site.SourceTree ?? context.Node.SyntaxTree;
        return site.SpanStart >= 0 && site.SpanLength >= 0 &&
               site.SpanStart + site.SpanLength <= tree.GetText(context.CancellationToken).Length
            ? Location.Create(tree, new TextSpan(site.SpanStart, site.SpanLength))
            : AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
    }
}
