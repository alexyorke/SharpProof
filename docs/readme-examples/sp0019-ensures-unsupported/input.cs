#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("local > 0")]
    public int Value(int input)
    {
        var local = input + 1;
        return local;
    }
}
