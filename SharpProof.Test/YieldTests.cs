using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class YieldTests
{
    [Test]
    public async Task PureMethodWithYield_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> GetNumbers()
    {
        yield return 1;
        yield return 2;
        yield return 3;
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImpureMethodWithYield_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    private int _state = 0;

    [EnforcePure]
    public IEnumerable<int> GetNumbers()
    {
        _state++;
        yield return _state;
    }
}";
        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(11, 29, 11, 39)
            .WithArguments("GetNumbers");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task PureMethodWithYieldAndImpureOperation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> GetNumbers()
    {
        Console.WriteLine(""Generating numbers"");
        yield return 1;
        yield return 2;
    }
}";
        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule.Id)
            .WithSpan(9, 29, 9, 39)
            .WithArguments("GetNumbers");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}