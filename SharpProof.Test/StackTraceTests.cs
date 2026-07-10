using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class StackTraceTests
{
    [Test]
    public async Task StackFrameGetMethod_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Diagnostics;
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodBase? {|SP0002:TestMethod|}(StackFrame stackFrame)
    {
        return stackFrame.GetMethod();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StackTraceConstructor_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public StackTrace {|SP0002:TestMethod|}()
    {
        return new StackTrace();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}