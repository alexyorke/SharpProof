using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class OutParameterTests
{
    [Test]
    public async Task PureMethodWithOutParameter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(out int x)
    {
        x = 10; // Impure operation - writing to an out parameter
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethodWithMultipleOutParameters_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(out int x, out string y)
    {
        x = 10;
        y = ""hello"";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethodWithTryPattern_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TryParse|}(string input, out int result)
    {
        if (int.TryParse(input, out result))
        {
            return true;
        }
        result = 0;
        return false;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task KnownPureEnumTryParseWithLocalOut_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return Enum.TryParse<DayOfWeek>(input, out _);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task KnownPureEnumTryParseWithNamedLocalOut_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        var parsed = DayOfWeek.Sunday;
        return Enum.TryParse<DayOfWeek>(input, out parsed);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task KnownPureEnumTryParseIgnoreCaseWithNamedLocalOut_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        var parsed = DayOfWeek.Sunday;
        return Enum.TryParse<DayOfWeek>(input, true, out parsed);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task KnownPureBoolTryParseNullableStringWithDiscardOut_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string? input)
    {
        return bool.TryParse(input, out _);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task KnownPureBoolTryParseNullableStringWithLocalOut_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string? input)
    {
        var parsed = false;
        return bool.TryParse(input, out parsed);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task KnownPureBoolParseString_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string input)
    {
        return bool.Parse(input);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task BoolTryParseWithFieldOut_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    private bool _result;

    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string? input)
    {
        return bool.TryParse(input, out _result);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task KnownPureEnumTryParseWithFieldOut_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private DayOfWeek _result;

    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string input)
    {
        return Enum.TryParse<DayOfWeek>(input, out _result);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethodCallingMethodWithOutParameter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    private void HelperMethod(out int x)
    {
        x = 42;
    }

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        int result;
        HelperMethod(out result);
        return result;
    }
}";
        var expectedTest = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(8, 18, 8, 30)
            .WithArguments("HelperMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedTest);
    }

    [Test]
    public async Task PureMethodWithOutVarDeclaration_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    private void HelperMethod(out int x)
    {
        x = 42;
    }

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        HelperMethod(out var result);
        return result;
    }
}";
        var expectedTest = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(8, 18, 8, 30)
            .WithArguments("HelperMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedTest);
    }

    [Test]
    public async Task PureMethodWithDiscardedOutParameter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    private void HelperMethod(out int x, out int y)
    {
        x = 42;
        y = 100;
    }

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        int result;
        HelperMethod(out result, out _);
        return result;
    }
}";
        var expectedTest = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(8, 18, 8, 30)
            .WithArguments("HelperMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedTest);
    }
}