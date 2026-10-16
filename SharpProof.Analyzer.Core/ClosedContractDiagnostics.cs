namespace SharpProof.Analyzer;

internal static class ClosedContractDiagnostics
{
    internal static void Validate(IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        if (method is null)
        { throw new System.ArgumentNullException(nameof(method)); }
        if (session is null)
        { throw new System.ArgumentNullException(nameof(session)); }
        if (reportDiagnostic is null)
        { throw new System.ArgumentNullException(nameof(reportDiagnostic)); }

        foreach (var site in ClosedContractAttributeValidator.EnumerateValueSites(
                     method,
                     includeReturn: !method.ReturnsVoid))
        {
            foreach (var attribute in site.Attributes)
            {
                var validation = ClosedContractAttributeValidator.Validate(
                    attribute,
                    site.Type,
                    site.RefKind,
                    session.Attributes);
                if (!validation.IsRecognized ||
                    validation.IsValid ||
                    !session.TryMarkAttributeValidated(attribute))
                {
                    continue;
                }

                var reference = attribute.ApplicationSyntaxReference;
                reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                    validation.AttributeName,
                    site.Type.Name,
                    validation.InvalidReason!,
                    reference?.SyntaxTree.GetLocation(reference.Span) ??
                    site.Fallback));
            }
        }
    }
}
