using System;
using SharpProof.Attributes;
public static class TestClass
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int Work(int n)
    {
        _ = Environment.GetEnvironmentVariable("PATH");
        return n;
    }
}
