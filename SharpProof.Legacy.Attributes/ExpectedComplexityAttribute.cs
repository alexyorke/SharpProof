namespace SharpProof.Attributes;

[Obsolete(
    "Complexity analysis was removed from SharpProof 0.2. " +
    "Remove this annotation; it is not proof-producing.")]
[AttributeUsage(
    AttributeTargets.Method |
    AttributeTargets.Constructor |
    AttributeTargets.Property,
    Inherited = false)]
public sealed class ExpectedComplexityAttribute(
    ComplexityKind maximumComplexity) : Attribute {
    public ComplexityKind MaximumComplexity { get; } = maximumComplexity;
}
