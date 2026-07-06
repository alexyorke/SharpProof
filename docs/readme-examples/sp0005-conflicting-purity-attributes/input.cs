#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    [Impure]
    public int Value()
    {
        return 1;
    }
}
