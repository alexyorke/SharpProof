using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class StaticStateInteractionTests
{
    [Test]
    public async Task InteractionWithStaticState_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public static class Counter
{
    private static int _count = 0;

    [EnforcePure]
    public static int Increment()
    {
        _count++;
        return _count;
    }

    [EnforcePure]
    public static int GetCount() // Reading mutable static is flagged
    {
        return _count;
    }

    [EnforcePure]
    public static void Reset()
    {
         _count = 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int UseCounter() // Calls impure Increment
    {
        Counter.Increment();
        return Counter.GetCount();
    }

    [EnforcePure]
    public int GetCurrentCountPurely() // Calls impure GetCount
    {
         return Counter.GetCount();
    }
}
";

        var expectedIncrement = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(9, 23, 9, 32)
            .WithArguments("Increment");
        var expectedGetCount = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(16, 23, 16, 31)
            .WithArguments("GetCount");
        var expectedReset = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(22, 24, 22, 29)
            .WithArguments("Reset");
        var expectedUseCounter = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(31, 16, 31, 26)
            .WithArguments("UseCounter");
        var expectedGetCurrentCountPurely = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(38, 16, 38, 37)
            .WithArguments("GetCurrentCountPurely");

        await VerifyCS.VerifyAnalyzerAsync(test,
            expectedIncrement,
            expectedGetCount,
            expectedReset,
            expectedUseCounter,
            expectedGetCurrentCountPurely);
    }

    [Test]
    public async Task StaticHelpersUsedByInstance_Diagnostics()
    {
        var test = @"
using SharpProof.Attributes;
using System;

public static class MathUtils
{
    [EnforcePure]
    public static int Add(int x, int y) => x + y; // Pure

    [EnforcePure]
    public static void LogCalculation(string op, int r) // Impure
    {
        Console.WriteLine($""{op} result: {r}"");
    }
}

public class Calculator
{
    private int _lastResult;

    [EnforcePure]
    public int CalculatePure(int a, int b) // Pure
    {
        int sum = MathUtils.Add(a, b);
        _lastResult = sum; // Allowed in pure methods if field is mutable? Let's assume it's impure. -> Update: Field assignment makes it impure.
        return sum;
    }

    [EnforcePure]
    public int CalculateAndLog(int a, int b) // Impure (calls LogCalculation)
    {
        int sum = MathUtils.Add(a, b);
        MathUtils.LogCalculation(""Add"", sum);
        _lastResult = sum; // Also impure
        return sum;
    }
}
";


        var expectedLogCalculation = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(11, 24, 11, 38)
            .WithArguments("LogCalculation");
        var expectedCalculatePure = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(22, 16, 22, 29)
            .WithArguments("CalculatePure");
        var expectedCalculateAndLog = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(30, 16, 30, 31)
            .WithArguments("CalculateAndLog");


        await VerifyCS.VerifyAnalyzerAsync(test,
            expectedLogCalculation,
            expectedCalculatePure,
            expectedCalculateAndLog);
    }
}