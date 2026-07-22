using System;
using SharpProof.Attributes;
public sealed class Worker
{
    [DoesNotThrow]
    public void Run()
    {
        throw new InvalidOperationException();
    }
}
