#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Calculator
{
    [Requires("true")]
    public int Value => 42;
}
