using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class UserDefinedIncrementOperatorTests
{
    [Test]
    public async Task UserDefinedIncrementOperator_WithImpureBody_ReportsSp0002()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public struct Counter
{
    private static int _writes;
    public int Value { get; }

    public Counter(int value)
    {
        Value = value;
    }

    public static Counter operator ++(Counter value)
    {
        _writes++;
        return new Counter(value.Value + 1);
    }
}

public static class TestClass
{
    [EnforcePure]
    public static Counter Bump(Counter counter)
    {
        counter++;
        return counter;
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(26, 27, 26, 31)
            .WithArguments("Bump");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task UserDefinedIncrementOperator_WithPureBody_RemainsPure()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public readonly struct Counter
{
    public int Value { get; }

    public Counter(int value)
    {
        Value = value;
    }

    public static Counter operator ++(Counter value)
    {
        return new Counter(value.Value + 1);
    }
}

public static class TestClass
{
    [EnforcePure]
    public static Counter Bump(Counter counter)
    {
        counter++;
        return counter;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}