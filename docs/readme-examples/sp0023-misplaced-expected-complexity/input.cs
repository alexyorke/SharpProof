#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [ExpectedComplexity(ComplexityKind.Constant)]
    public int Value { get; set; }
}
