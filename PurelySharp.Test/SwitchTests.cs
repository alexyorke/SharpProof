using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;
using PurelySharp.Attributes;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SwitchTests
    {
        [Test]
        public async Task PureMethodWithSwitch_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 1:
                return 10;
            case 2:
                return 20;
            case 3:
                return 30;
            default:
                return 0;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImpureMethodWithSwitch_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    private int _state;

    [EnforcePure]
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 1:
                _state++;
                return 10;
            case 2:
                return 20;
            default:
                return 0;
        }
    }
}";


            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                  .WithSpan(10, 16, 10, 26)
                                  .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithSwitchAndImpureOperation_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 1:
                Console.WriteLine(""Case 1""); // Impure operation
                return 10;
            case 2:
                return 20;
            default:
                return 0;
        }
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                   .WithSpan(8, 16, 8, 26)
                                   .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithImpureSwitchValue_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        switch (Console.Read())
        {
            case 1:
                return 10;
            default:
                return 0;
        }
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                   .WithSpan(8, 16, 8, 26)
                                   .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithImpureSwitchCaseGuard_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0 when Console.Read() > 0:
                return 1;
            default:
                return 2;
        }
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                   .WithSpan(8, 16, 8, 26)
                                   .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ConstantSwitchFalseGuardWithoutDefault_IgnoresDeadImpureSection()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        switch (1)
        {
            case 1 when false:
                Console.WriteLine(""dead"");
                return 1;
        }

        return 2;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConstantSwitchExpressionWithRuntimeGuard_DoesNotDropReachableArmValue()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(bool flag)
    {
        return 1 switch
        {
            1 when flag => Console.Read(),
            _ => 0
        };
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ExhaustiveSwitchExpressionWithDiscardArm_IgnoresCompilerFallbackException()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(ReadOnlySpan<char> value)
    {
        return value switch
        {
            ""true"" => ""yes"",
            ""false"" => ""no"",
            var text when text.Length > 0 => ""other"",
            _ => ""empty""
        };
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}


