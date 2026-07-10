using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ArrayFactoryTests
{
    [Test]
    public async Task ArrayEmptyReturned_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        return Array.Empty<int>();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayEmptyStandaloneInvocation_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        _ = Array.Empty<int>();
        return 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayEmptyConditionalReturned_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod(bool useLeft)
    {
        return useLeft ? Array.Empty<int>() : Array.Empty<int>();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayEmptyLocalReturned_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        var values = Array.Empty<int>();
        return values;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayEmptyConstantConditionalWithDeadArrayFactory_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod()
    {
        return true ? Array.Empty<byte>() : BitConverter.GetBytes(1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}