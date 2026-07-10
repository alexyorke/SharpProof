using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class SearchLibBackedPurityFlowTests
{
    [Test]
    public async Task ContradictoryNestedGuardedImpureCall_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int x)
    {
        if (x > 0)
        {
            if (x < 0)
            {
                Console.WriteLine(x);
            }
        }

        return x;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ReachableNestedGuardedImpureCall_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(int x)
    {
        if (x > 0)
        {
            if (x >= 0)
            {
                Console.WriteLine(x);
            }
        }

        return x;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}