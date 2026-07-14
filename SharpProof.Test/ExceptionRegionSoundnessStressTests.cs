using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ExceptionRegionSoundnessStressTests
{
    [Test]
    public async Task CatchFilterImpureCondition_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(int dividend, int divisor)
    {
        try
        {
            return dividend / divisor;
        }
        catch (DivideByZeroException) when (DateTime.Now.Millisecond >= 0)
        {
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CatchFilterPureCondition_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int dividend, int divisor)
    {
        try
        {
            return dividend / divisor;
        }
        catch (DivideByZeroException) when (divisor == 0)
        {
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CatchBodyImpure_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(int dividend, int divisor)
    {
        try
        {
            return dividend / divisor;
        }
        catch (DivideByZeroException)
        {
            Console.WriteLine(divisor);
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CatchBodyPure_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int dividend, int divisor)
    {
        try
        {
            return dividend / divisor;
        }
        catch (DivideByZeroException)
        {
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FinallyBodyImpure_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(int value)
    {
        try
        {
            return value;
        }
        finally
        {
            Console.WriteLine(value);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FinallyBodyLocalMutation_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        var local = value;
        try
        {
            return local;
        }
        finally
        {
            local++;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EmptyFinally_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        try
        {
            return value + 1;
        }
        finally
        {
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}