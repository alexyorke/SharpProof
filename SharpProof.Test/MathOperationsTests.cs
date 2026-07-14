using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class MathOperationsTests
{
    [Test]
    public async Task ComplexNestedExpressions_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.ComplexNestedExpressions);
    }

    [Test]
    public async Task SimpleMathMethod_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.SimpleMathMethod);
    }

    [Test]
    public async Task MathConstant_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.MathConstant);
    }

    [Test]
    public async Task MathMethodChain_NoDiagnostic()
    {
        await VerifyCS.VerifyAnalyzerAsync(MathAndAttributeTestSources.MathMethodChain);
    }

    [Test]
    public async Task MathDoubleHelpers_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double CeilingMethod(double x)
    {
        return Math.Ceiling(x);
    }

    [EnforcePure]
    public double FloorMethod(double x)
    {
        return Math.Floor(x);
    }

    [EnforcePure]
    public double TruncateMethod(double x)
    {
        return Math.Truncate(x);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MathSignDecimal_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(decimal x)
    {
        return Math.Sign(x);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MathCeilingDecimal_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public decimal TestMethod(decimal x)
    {
        return Math.Ceiling(x);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MathAbsOverloads_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public decimal TestDecimal(decimal x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public double TestDouble(double x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public float TestFloat(float x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public int TestInt(int x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public long TestLong(long x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public nint TestNInt(nint x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public sbyte TestSByte(sbyte x)
    {
        return Math.Abs(x);
    }

    [EnforcePure]
    public short TestShort(short x)
    {
        return Math.Abs(x);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
