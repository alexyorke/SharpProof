using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class TimeSpanTests
{
    [Test]
    public async Task TimeSpanConstructor_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod()
    {
        return new TimeSpan(1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task TimeSpanAdd_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|SP0002:TestMethod|}(TimeSpan left, TimeSpan right)
    {
        return left.Add(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task TimeSpanCompareTo_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(TimeSpan left, TimeSpan right)
    {
        return left.CompareTo(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task TimeSpanFromDays_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod(double days)
    {
        return TimeSpan.FromDays(days);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}