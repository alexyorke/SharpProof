namespace SharpProof.Analyzer;

internal static class ClosedContractDiagnostics
{
    internal static void Validate(IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var parameter in method.Parameters)
        {
            ValidateValue(
                parameter.Type,
                parameter.RefKind,
                parameter.GetAttributes(),
                parameter.Locations.FirstOrDefault() ?? Location.None);
        }

        if (!method.ReturnsVoid)
        {
            ValidateValue(
                method.ReturnType,
                RefKind.None,
                method.GetReturnTypeAttributes(),
                method.Locations.FirstOrDefault() ?? Location.None);
        }

        void ValidateValue(
            ITypeSymbol type,
            RefKind refKind,
            ImmutableArray<AttributeData> attributes,
            Location fallback)
        {
            foreach (var attribute in attributes)
            {
                var validation = ClosedContractAttributeValidator.Validate(
                    attribute,
                    type,
                    refKind,
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
                    type.Name,
                    validation.InvalidReason!,
                    reference?.SyntaxTree.GetLocation(reference.Span) ??
                    fallback));
            }
        }
    }
}
