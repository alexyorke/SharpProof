using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class RandomOverloadTests
{
    [Test]
    public async Task RandomSharedNextInt64_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Random.Shared.NextInt64();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RandomSharedNextInt64Max_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Random.Shared.NextInt64(10);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RandomSharedNextInt64Range_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Random.Shared.NextInt64(1, 11);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RandomSharedProperty_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Random {|SP0002:TestMethod|}()
    {
        return Random.Shared;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RandomSharedNext_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Random.Shared.Next();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RandomSharedNextDouble_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public double {|SP0002:TestMethod|}()
    {
        return Random.Shared.NextDouble();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RandomSharedNextMaxValue_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Random.Shared.Next(10);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RandomNextRange_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(Random random)
    {
        return random.Next(1, 11);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}