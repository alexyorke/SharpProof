using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class NonDeterministicOperationsTests
{
    [Test]
    public async Task ImpureMethodWithRandomOperation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return new Random().Next();
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(10, 16, 10, 26)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}