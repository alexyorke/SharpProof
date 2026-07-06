#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    [AllowSynchronization]
    public int Add(int left, int right)
    {
        return left + right;
    }
}
