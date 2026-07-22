using System;
using SharpProof.Attributes;
public sealed class TestClass
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public void TestMethod()
    {
        Console.WriteLine("hello");
    }
}
