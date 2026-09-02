namespace SharpProof.Contracts;

internal enum ClosedContractAttributeKind
{
    None,
    NotNull,
    Positive,
    InRange
}

internal readonly struct ClosedContractAttributeValidation(
    ClosedContractAttributeKind kind,
    string? invalidReason = null,
    long minimum = default,
    long maximum = default)
{
    internal ClosedContractAttributeKind Kind { get; } = kind;
    internal string? InvalidReason { get; } = invalidReason;
    internal long Minimum { get; } = minimum;
    internal long Maximum { get; } = maximum;
    internal bool IsRecognized => Kind != ClosedContractAttributeKind.None;
    internal bool IsValid => IsRecognized && InvalidReason == null;

    internal string AttributeName => ContractProjections.ClosedAttributeName(Kind);
}

internal static class ClosedContractAttributeValidator
{
    internal static ClosedContractAttributeValidation Validate(
        AttributeData attribute,
        ITypeSymbol type,
        RefKind refKind,
        ContractSelectionInventory symbols)
    {
        var kind = symbols.GetClosedContractKind(attribute);
        if (kind == ClosedContractAttributeKind.None)
        {
            return default;
        }

        if (refKind == RefKind.Out)
        {
            return Invalid(
                kind,
                "expected an incoming parameter value; out parameters have no entry value");
        }

        return kind switch
        {
            ClosedContractAttributeKind.NotNull when !type.IsReferenceType =>
                Invalid(kind, "expected a definitely reference-capable value"),
            ClosedContractAttributeKind.Positive when !IsSupportedInteger(type) =>
                Invalid(kind, "expected a supported integral value"),
            ClosedContractAttributeKind.InRange =>
                ValidateRange(attribute, type),
            _ => new ClosedContractAttributeValidation(kind)
        };
    }

    private static ClosedContractAttributeValidation ValidateRange(
        AttributeData attribute,
        ITypeSymbol type)
    {
        if (!IsSupportedInteger(type) ||
            attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not long minimum ||
            attribute.ConstructorArguments[1].Value is not long maximum ||
            minimum > maximum)
        {
            return Invalid(
                ClosedContractAttributeKind.InRange,
                "expected a supported integral value and ordered bounds");
        }

        return new ClosedContractAttributeValidation(
            ClosedContractAttributeKind.InRange,
            minimum: minimum,
            maximum: maximum);
    }

    private static ClosedContractAttributeValidation Invalid(
        ClosedContractAttributeKind kind,
        string reason)
    {
        return new ClosedContractAttributeValidation(kind, reason);
    }

    private static bool IsSupportedInteger(ITypeSymbol type)
    {
        return CSharpScalarSemantics.IsSupportedInteger(type.SpecialType);
    }
}
