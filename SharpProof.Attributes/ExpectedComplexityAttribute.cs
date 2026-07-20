namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.All, Inherited = false)]
public sealed class ExpectedComplexityAttribute : Attribute {
    public ExpectedComplexityAttribute(ComplexityKind maximumComplexity) {
        MaximumComplexity = maximumComplexity;
    }

    public ComplexityKind MaximumComplexity { get; }
}