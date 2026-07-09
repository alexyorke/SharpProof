#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Worker
{
    [DoesNotThrow]
    public int Value => 42;
}
