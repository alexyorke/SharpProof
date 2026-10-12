namespace SharpProof.Contracts;

internal enum ClosedContractAttributeKind
{
    None,
    NotNull,
    Positive,
    InRange
}

internal readonly struct ClosedContractAttributeValidation(
    ClosedContractAttributeClassification classification,
    string? invalidReason = null,
    long minimum = default,
    long maximum = default)
{
    private readonly ClosedContractAttributeClassification? _classification =
        ArgumentNullGuard.NotNull(classification, nameof(classification));

    internal ClosedContractAttributeKind Kind => _classification?.Kind ?? ClosedContractAttributeKind.None;
    internal string? InvalidReason { get; } = invalidReason;
    internal long Minimum { get; } = minimum;
    internal long Maximum { get; } = maximum;
    internal bool IsRecognized => _classification != null;
    internal bool IsValid => IsRecognized && InvalidReason == null;

    internal string AttributeName =>
        ArgumentNullGuard.NotNull(_classification, nameof(_classification)).AttributeName;

    internal bool SupportsLegacyValue(IrFactory factory, IrTerm value)
    {
        return ArgumentNullGuard.NotNull(_classification, nameof(_classification)).SupportsLegacyValue(factory, value);
    }

    internal IrTerm CreateLegacyPredicate(IrFactory factory, IrTerm value)
    {
        return ArgumentNullGuard.NotNull(_classification, nameof(_classification)).CreateLegacyPredicate(factory, value, Minimum, Maximum);
    }
}

internal static class ClosedContractAttributeValidator
{
    internal static IEnumerable<(
        ITypeSymbol Type,
        RefKind RefKind,
        ImmutableArray<AttributeData> Attributes,
        Location Fallback,
        int ParameterIndex,
        bool IsReturn)> EnumerateValueSites(
        IMethodSymbol method,
        bool includeReturn)
    {
        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var parameter = method.Parameters[index];
            yield return (
                parameter.Type,
                parameter.RefKind,
                parameter.GetAttributes(),
                parameter.Locations.FirstOrDefault() ?? Location.None,
                index,
                false);
        }

        if (includeReturn)
        {
            yield return (
                method.ReturnType,
                RefKind.None,
                method.GetReturnTypeAttributes(),
                method.Locations.FirstOrDefault() ?? Location.None,
                -1,
                true);
        }
    }

    internal static ClosedContractAttributeValidation Validate(
        AttributeData attribute,
        ITypeSymbol type,
        RefKind refKind,
        ContractSelectionInventory symbols,
        bool includeUnsigned64 = false)
    {
        var classification = symbols.GetClosedContractClassification(attribute);
        if (classification == null)
        {
            return default;
        }

        if (refKind == RefKind.Out)
        {
            return Invalid(
                classification,
                "expected an incoming parameter value; out parameters have no entry value");
        }

        return classification.Kind switch
        {
            ClosedContractAttributeKind.NotNull when !CanBeNull(type) =>
                Invalid(classification, "expected a nullable or reference-capable value"),
            ClosedContractAttributeKind.Positive when !IsSupportedInteger(type, includeUnsigned64) =>
                Invalid(classification, "expected a supported integral value"),
            ClosedContractAttributeKind.InRange =>
                ValidateRange(attribute, type, includeUnsigned64),
            _ => new ClosedContractAttributeValidation(classification)
        };
    }

    private static ClosedContractAttributeValidation ValidateRange(
        AttributeData attribute,
        ITypeSymbol type,
        bool includeUnsigned64)
    {
        if (!IsSupportedInteger(type, includeUnsigned64) ||
            attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not long minimum ||
            attribute.ConstructorArguments[1].Value is not long maximum ||
            minimum > maximum)
        {
            return Invalid(
                ClosedContractAttributeClassification.InRange,
                "expected a supported integral value and ordered bounds");
        }

        return new ClosedContractAttributeValidation(
            ClosedContractAttributeClassification.InRange,
            minimum: minimum,
            maximum: maximum);
    }

    private static ClosedContractAttributeValidation Invalid(
        ClosedContractAttributeClassification classification,
        string reason)
    {
        return new ClosedContractAttributeValidation(classification, reason);
    }

    private static bool IsSupportedInteger(ITypeSymbol type, bool includeUnsigned64)
    {
        return CSharpOperationSemantics.IsSupportedInteger(type.SpecialType) ||
            includeUnsigned64 && type.SpecialType == SpecialType.System_UInt64;
    }

    private static bool CanBeNull(ITypeSymbol type)
    {
        if (type.IsReferenceType)
        {
            return true;
        }

        return type switch
        {
            INamedTypeSymbol named =>
                named.OriginalDefinition.SpecialType ==
                    SpecialType.System_Nullable_T,
            ITypeParameterSymbol parameter =>
                !parameter.HasValueTypeConstraint &&
                !parameter.HasUnmanagedTypeConstraint,
            _ => false
        };
    }
}
