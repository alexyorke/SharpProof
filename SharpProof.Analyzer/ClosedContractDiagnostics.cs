namespace SharpProof.Analyzer;

internal static class ClosedContractDiagnostics
{
    internal static void Validate(IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var parameter in method.Parameters)
        {
            ValidateValue(
                parameter.Type, parameter.GetAttributes(),
                parameter.Locations.FirstOrDefault() ?? Location.None);
        }

        if (!method.ReturnsVoid)
        {
            ValidateValue(
                method.ReturnType, method.GetReturnTypeAttributes(),
                method.Locations.FirstOrDefault() ?? Location.None);
        }

        void ValidateValue(ITypeSymbol type, ImmutableArray<AttributeData> attributes, Location fallback)
        {
            foreach (var attribute in attributes)
            {
                var error = GetError(type, attribute, session.Attributes);
                if (!error.HasValue ||
                    !session.TryMarkAttributeValidated(attribute))
                {
                    continue;
                }

                var reference = attribute.ApplicationSyntaxReference;
                reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                    error.Value.Name, type.Name, error.Value.Reason,
                    reference?.SyntaxTree.GetLocation(reference.Span) ??
                    fallback));
            }
        }
    }

    private static (string Name, string Reason)? GetError(ITypeSymbol type, AttributeData attribute, ContractSelectionInventory symbols)
    {
        return ContractSelectionInventory.Is(attribute, symbols.NotNull) &&
        type.IsValueType
            ? ("[NotNull]", "expected a reference-capable value")
            : ContractSelectionInventory.Is(attribute, symbols.Positive) &&
              !IsSupportedInteger(type)
                ? ("[Positive]", "expected a supported integral value")
                : ContractSelectionInventory.Is(attribute, symbols.InRange) &&
                  (!IsSupportedInteger(type) ||
                   attribute.ConstructorArguments.Length != 2 ||
                   attribute.ConstructorArguments[0].Value is not long minimum ||
                   attribute.ConstructorArguments[1].Value is not long maximum ||
                   minimum > maximum)
                    ? ("[InRange]", "expected a supported integral value and ordered bounds")
                    : null;
    }

    private static bool IsSupportedInteger(ITypeSymbol type)
    {
        return CSharpScalarSemantics.IsSupportedInteger(type.SpecialType);
    }
}
