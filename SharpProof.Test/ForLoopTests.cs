using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ForLoopTests
{
    [Test]
    public async Task ForLoopImpureInitializerWithConstantFalseCondition_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        for (var i = GetStart(); false; i++)
        {
        }
    }

    private int GetStart()
    {
        Console.WriteLine(""start"");
        return 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ForLoopImpureIncrementor_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        for (var i = 0; i < 1; i = Next(i))
        {
        }
    }

    private int Next(int value)
    {
        Console.WriteLine(""next"");
        return value + 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}