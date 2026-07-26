namespace SharpProof.Analyzer;

internal static class SharpProofControlAttributePolicy {
    internal static bool ValidateAndShouldSuppress(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        var suppress = false;
        foreach (var symbol in EnumerateScopes(method)) {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in symbol.GetAttributes()) {
                if (AnalyzerAttributeSymbols.Is(
                        attribute,
                        session.Attributes.Suppress)) {
                    if (TryGetReason(attribute, out var reason))
                        suppress = true;
                    else
                        ReportInvalidReason(
                            method,
                            attribute,
                            "[SharpProofSuppress]",
                            reason,
                            session,
                            reportDiagnostic,
                            cancellationToken);
                }
                else if (AnalyzerAttributeSymbols.Is(
                             attribute,
                             session.Attributes.Trusted) &&
                         !TryGetReason(attribute, out var reason)) {
                    ReportInvalidReason(
                        method,
                        attribute,
                        "[SharpProofTrusted]",
                        reason,
                        session,
                        reportDiagnostic,
                        cancellationToken);
                }
            }
        }
        return suppress;
    }

    private static IEnumerable<ISymbol> EnumerateScopes(IMethodSymbol method) {
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
        IMethodSymbol method,
        AttributeData attribute,
        string attributeName,
        string reason,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        if (!session.TryMarkAttributeValidated(attribute)) return;
        var location =
            attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(method, cancellationToken);
        reportDiagnostic(
            InvalidContractArgumentDiagnostics.Create(
                attributeName,
                string.IsNullOrEmpty(reason) ? "<empty>" : reason,
                "expected a non-empty reason",
                location));
    }
}
