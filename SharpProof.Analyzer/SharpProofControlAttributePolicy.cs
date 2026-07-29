namespace SharpProof.Analyzer;

internal static class SharpProofControlAttributePolicy {
    internal static bool ValidateAndShouldSuppress(
        IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        var suppress = false;
        foreach (var symbol in EnumerateScopes(method)) {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in symbol.GetAttributes()) {
                var suppressing = IsSuppressing(attribute, session.Attributes);
                if (!suppressing.HasValue) continue;
                if (TryGetReason(attribute, out var reason)) {
                    suppress |= suppressing.Value;
                    continue;
                }
                ReportInvalidReason(
                    method, attribute, suppressing.Value, reason, session,
                    reportDiagnostic, cancellationToken);
            }
        }
        return suppress;
    }

    internal static IEnumerable<ISymbol> EnumerateScopes(IMethodSymbol method) {
        yield return method;
        if (method.AssociatedSymbol is IPropertySymbol property)
            yield return property;
        for (var type = method.ContainingType; type != null; type = type.ContainingType)
            yield return type;
        if (method.ContainingAssembly != null)
            yield return method.ContainingAssembly;
    }

    private static bool TryGetReason(AttributeData attribute, out string reason) {
        reason = attribute.ConstructorArguments.Length == 1 &&
                 attribute.ConstructorArguments[0].Value is string value
            ? value
            : string.Empty;
        return !string.IsNullOrWhiteSpace(reason);
    }

    private static void ReportInvalidReason(
        IMethodSymbol method, AttributeData attribute, bool suppressing,
        string reason, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        if (!session.TryMarkAttributeValidated(attribute)) return;
        var location =
            attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(method, cancellationToken);
        reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
            suppressing ? "[SharpProofSuppress]" : "[SharpProofTrusted]",
            string.IsNullOrEmpty(reason) ? "<empty>" : reason,
            "expected a non-empty reason",
            location));
    }

    private static bool? IsSuppressing(
        AttributeData attribute,
        ContractSelectionInventory inventory) =>
        ContractSelectionInventory.Is(attribute, inventory.Suppress)
            ? true
            : ContractSelectionInventory.Is(attribute, inventory.Trusted)
                ? false
                : null;
}
