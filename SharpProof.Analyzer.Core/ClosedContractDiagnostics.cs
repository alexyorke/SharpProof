namespace SharpProof.Analyzer;

internal static class ClosedContractDiagnostics
{
    internal static bool Validate(IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        var hasValidContract = false;
        foreach (var parameter in method.Parameters)
        {
            ValidateValue(
                parameter.Type,
                parameter.RefKind,
                parameter.GetAttributes(),
                parameter.Locations.FirstOrDefault() ?? Location.None,
                parameter);
        }

        ValidateValue(
            method.ReturnType,
            RefKind.None,
            method.GetReturnTypeAttributes(),
            method.Locations.FirstOrDefault() ?? Location.None,
            method);

        void ValidateValue(
            ITypeSymbol type,
            RefKind refKind,
            ImmutableArray<AttributeData> attributes,
            Location fallback,
            ISymbol owner)
        {
            foreach (var attribute in attributes)
            {
                var validation = ClosedContractAttributeValidator.Validate(
                    attribute,
                    type,
                    refKind,
                    session.Attributes);
                if (!validation.IsRecognized)
                {
                    continue;
                }
                if (validation.IsValid)
                {
                    hasValidContract = true;
                    continue;
                }
                if (!session.TryMarkAttributeValidated(attribute, owner))
                {
                    continue;
                }

                var reference = attribute.ApplicationSyntaxReference;
                reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                    validation.AttributeName,
                    type.Name,
                    validation.InvalidReason!,
                    reference?.SyntaxTree.GetLocation(reference.Span) ??
                    fallback));
            }
        }

        return hasValidContract;
    }
}
