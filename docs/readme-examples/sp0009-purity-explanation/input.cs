#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void Log()
    {
        Console.WriteLine("hello");
    }
}
