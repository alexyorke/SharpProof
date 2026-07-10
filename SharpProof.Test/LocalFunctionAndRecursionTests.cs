using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class LocalFunctionAndRecursionTests
{
    [Test]
    public async Task ImpureLocalFunction_FieldModification_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private int _field;

    [EnforcePure]
    public int TestMethod()
    {
        int LocalFunction()
        {
            _field++; // Local function modifies field
            return _field;
        }

        return LocalFunction();
    }
}";


        var expectedOuter = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(12, 16, 12, 26)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedOuter);
    }

    [Test]
    public async Task MethodWithRecursiveImpureCall_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void TestMethod(int n)
    {
        if (n <= 0) return;
        Console.WriteLine(n); // Impure operation
        TestMethod(n - 1); // Recursive call
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(10, 17, 10, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task PureMethodCallingImpureMethod_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private void ImpureMethod()
    {
        Console.WriteLine(""Impure"");
    }

    [EnforcePure]
    public void TestMethod()
    {
        ImpureMethod(); // Calling impure method
    }
}";

        var expectedOuter = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(15, 17, 15, 27)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedOuter);
    }
}