using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class ToStringBehaviorTests
{
    public class MySimpleClass
    {
        public int Value { get; set; }
    }

    [Test]
    public async Task DefaultToStringCall_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace SharpProof.Test // Add namespace to match outer scope
{
    // Re-define class inside the test string scope
    public class MySimpleClass
    {
        public int Value { get; set; }
    }

    public class TestClass
    {
        [EnforcePure]
        public string CallDefaultToString()
        {
            var instance = new MySimpleClass { Value = 42 };
            // Calling the default object.ToString() implementation
            return instance.ToString(); // Line 20
        }
    }
}";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(16, 23, 16, 42)
            .WithArguments("CallDefaultToString");


        var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(10, 20, 10, 25)
            .WithArguments("get_Value");


        await VerifyCS.VerifyAnalyzerAsync(test, expected, expectedGetter);
    }

    [Test]
    public async Task ObjectToStringOnParameter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(object value)
    {
        return value.ToString();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}