namespace SharpProof.Analyzer;

internal static class SharpProofControlAttributePolicy
{
    internal static bool ValidateAndShouldSuppress(
        IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var suppress = false;
        foreach (var symbol in EnumerateScopes(method))
        {
            suppress |= ValidateScope(
                symbol, session, reportDiagnostic, cancellationToken);
        }
        return suppress;
    }

    internal static void ValidateDeclaredScope(
        ISymbol symbol, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        _ = ValidateScope(
            symbol, session, reportDiagnostic, cancellationToken);
    }

    internal static IEnumerable<ISymbol> EnumerateScopes(IMethodSymbol method)
    {
        yield return method;
        if (method.AssociatedSymbol is IPropertySymbol property)
        {
            yield return property;
        }

        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            yield return type;
        }

        if (method.ContainingAssembly != null)
        {
            yield return method.ContainingAssembly;
        }
    }

    private static bool TryGetReason(AttributeData attribute, out string reason)
    {
        reason = attribute.ConstructorArguments.Length == 1 &&
                 attribute.ConstructorArguments[0].Value is string value
            ? value
            : string.Empty;
        return !string.IsNullOrWhiteSpace(reason);
    }

    private static bool ValidateScope(
        ISymbol symbol, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var suppress = false;
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var attribute in symbol.GetAttributes())
        {
            var suppressing = IsSuppressing(attribute, session.Attributes);
            if (!suppressing.HasValue)
            {
                continue;
            }

            if (TryGetReason(attribute, out var reason))
            {
                suppress |= suppressing.Value;
                continue;
            }
            ReportInvalidReason(
                symbol, attribute, suppressing.Value, reason, session,
                reportDiagnostic, cancellationToken);
        }
        return suppress;
    }

    private static void ReportInvalidReason(
        ISymbol symbol, AttributeData attribute, bool suppressing,
        string reason, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        if (!session.TryMarkAttributeValidated(attribute))
        {
            return;
        }

        var location =
            attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
            symbol.Locations.FirstOrDefault(static candidate => candidate.IsInSource) ??
            Location.None;
        reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
            suppressing ? "[SharpProofSuppress]" : "[SharpProofTrusted]",
            string.IsNullOrEmpty(reason) ? "<empty>" : reason,
            "expected a non-empty reason",
            location));
    }

    private static bool? IsSuppressing(
        AttributeData attribute,
        ContractSelectionInventory inventory)
    {
        return ContractSelectionInventory.Is(attribute, inventory.Suppress)
            ? true
            : ContractSelectionInventory.Is(attribute, inventory.Trusted)
                ? false
                : null;
    }
}
