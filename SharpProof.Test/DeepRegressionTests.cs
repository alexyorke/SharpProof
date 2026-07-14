using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class DeepRegressionTests
{
    [Test]
    public async Task ConstantFalseWhile_IgnoresDeadImpureInvocation()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Run()
    {
        while (false)
        {
            Console.WriteLine(""dead"");
        }

        return 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantFalseFor_IgnoresDeadImpureInvocation()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Run()
    {
        for (var i = 0; false; i++)
        {
            Console.WriteLine(i);
        }

        return 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantFalseIf_IgnoresDeadThrow()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Run()
    {
        if (false)
        {
            throw new InvalidOperationException();
        }

        return 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantSwitchExpression_IgnoresUnmatchedImpureArm()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Run()
    {
        return 1 switch
        {
            1 => 42,
            _ => Console.Read()
        };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantSwitchStatement_IgnoresUnmatchedImpureSection()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Run()
    {
        switch (1)
        {
            case 1:
                return 42;
            default:
                Console.WriteLine(""dead"");
                return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantSwitchStatementPatternWhenTrue_ReachesImpureSection_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:Run|}()
    {
        switch (1)
        {
            case 1 when true:
                Console.WriteLine(""reachable"");
                return;
            default:
                return;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantSwitchStatementPatternWhenUnknown_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static bool ImpureCondition()
    {
        Console.WriteLine(""condition"");
        return true;
    }

    [EnforcePure]
    public void {|SP0002:Run|}()
    {
        switch (1)
        {
            case 1 when ImpureCondition():
                return;
            default:
                return;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantSwitchGotoDefault_ReachesThrow_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:Run|}()
    {
        switch (1)
        {
            case 1:
                goto default;
            default:
                throw new InvalidOperationException();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ShortCircuitFalseAnd_IgnoresUnreachableImpureRightOperand()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static bool ImpureCondition() => DateTime.Now.Ticks > 0;

    [EnforcePure]
    public int Run()
    {
        if (false && ImpureCondition())
        {
            return 0;
        }

        return 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ShortCircuitTrueOr_IgnoresUnreachableImpureRightOperand()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static bool ImpureCondition() => DateTime.Now.Ticks > 0;

    [EnforcePure]
    public int Run()
    {
        if (true || ImpureCondition())
        {
            return 1;
        }

        return 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantNonNullCoalesce_IgnoresUnreachableImpureRightOperand()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static string ImpureFallback()
    {
        Console.WriteLine(""impure"");
        return ""fallback"";
    }

    [EnforcePure]
    public string Run()
    {
        return ""fixed"" ?? ImpureFallback();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstantNullConditionalAccess_IgnoresUnreachableImpureWhenNotNull()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Service
{
    public int Impure()
    {
        Console.WriteLine(""impure"");
        return 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int Run()
    {
        ((Service)null)?.Impure();
        return 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}