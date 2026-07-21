using SharpProof.Attributes;

namespace SharpProof.Analyzer;

internal static class MethodCapabilityAnalyzer {
    internal static void AnalyzeSymbolForCapabilities(
        MethodBodyAnalysisContext context,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        if (!TryGetAllowedCapabilities(context, attributePolicy, out var allowed)) return;

        var method = context.MethodSymbol;
        var effects = context.State.GetMethodEffects(context.CancellationToken);
        foreach (var site in effects.Sites) {
            var disallowed = site.Capabilities & ~allowed;
            if (disallowed == SharpProofCapability.None) continue;

            var location = Location.Create(context.Node.SyntaxTree, new TextSpan(site.SpanStart, site.SpanLength));
            var text = disallowed.ToString();
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("CapabilityViolationRule"),
                location,
                site.Operation,
                method.Name,
                text));
        }

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

    private static bool TryGetAllowedCapabilities(
        MethodBodyAnalysisContext context,
        SharpProofAttributeIdentityPolicy attributePolicy,
        out SharpProofCapability allowed) {
        allowed = SharpProofCapability.None;
        var found = false;
        foreach (var source in MethodContractHierarchy.EnumerateSources(
                     context.MethodSymbol,
                     context.CancellationToken))
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(source, "AllowedCapabilitiesAttribute")) {
            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value == null)
                continue;
            var declared = (SharpProofCapability)Convert.ToInt32(
                attribute.ConstructorArguments[0].Value,
                CultureInfo.InvariantCulture);
            allowed = found ? allowed & declared : declared;
            found = true;
        }

        return found;
    }
}
