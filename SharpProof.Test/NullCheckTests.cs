using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class NullCheckTests
{
    [Test]
    public async Task PureMethodWithNullCheck_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        // Null check itself is pure
        if (input == null)
        {
            return ""default"";
        }
        return input;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImpureMethodWithNullCheck_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private static string _field = ""initial"";

    [EnforcePure]
    public string {|SP0002:TestMethod|}(string input)
    {
        if (input == null)
        {
            _field = ""modified"";
            return ""default"";
        }
        return input;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MethodWithNullCheckAndImpureOperation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private static string _field = ""initial"";

    [EnforcePure]
    public string {|SP0002:TestMethod|}(string input)
    {
        if (input == null)
        {
            Console.WriteLine(""Null input detected"");
            return ""default"";
        }
        return input;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}