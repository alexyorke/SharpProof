namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false)]
public sealed class ExpectedComplexityAttribute : Attribute {
    public ExpectedComplexityAttribute(ComplexityKind maximumComplexity) {
        MaximumComplexity = maximumComplexity;
    }

    public ComplexityKind MaximumComplexity { get; }
}
