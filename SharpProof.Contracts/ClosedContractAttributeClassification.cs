namespace SharpProof.Contracts;

internal sealed class ClosedContractAttributeClassification
{
    internal static readonly ClosedContractAttributeClassification NotNull =
        new(ClosedContractAttributeKind.NotNull, "[NotNull]");
    internal static readonly ClosedContractAttributeClassification Positive =
        new(ClosedContractAttributeKind.Positive, "[Positive]");
    internal static readonly ClosedContractAttributeClassification InRange =
        new(ClosedContractAttributeKind.InRange, "[InRange]");

    private ClosedContractAttributeClassification(ClosedContractAttributeKind kind, string attributeName)
    {
        Kind = kind;
        AttributeName = attributeName;
    }

    internal ClosedContractAttributeKind Kind { get; }
    internal string AttributeName { get; }
}
