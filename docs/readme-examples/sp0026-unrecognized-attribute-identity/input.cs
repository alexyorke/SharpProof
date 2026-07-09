using System;

namespace ExternalContracts
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class EnforcePureAttribute : Attribute
    {
    }
}

public sealed class TestClass
{
    [ExternalContracts.EnforcePure]
    public void NotSharpProof()
    {
        Console.WriteLine("not analyzed as a SharpProof contract");
    }
}
