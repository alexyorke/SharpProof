#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("true")]
    public int Value = 42;
}
