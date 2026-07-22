namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false)]
public sealed class ExpectedComplexityAttribute(ComplexityKind maximumComplexity) : Attribute {
    public ComplexityKind MaximumComplexity { get; } = maximumComplexity;
}
