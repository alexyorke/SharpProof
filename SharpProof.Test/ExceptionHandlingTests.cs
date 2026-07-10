using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ExceptionHandlingTests
{
    [Test]
    public async Task PureMethodWithExceptionHandling_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(int x)
    {
        try
        {
            // Exception handling itself is not impure; the throw branch is.
            if (x < 0)
            {
                throw new ArgumentException(""x cannot be negative"", nameof(x));
            }
            
            return x * 2;
        }
        catch (Exception ex)
        {
            // Reading the exception message is also pure
            string message = ex.Message;
            return 0;
        }
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImpureMethodWithExceptionHandlingAndImpureOperation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int TestMethod(int x)
    {
        try
        {
            if (x < 0)
            {
                throw new ArgumentException(""x cannot be negative"", nameof(x));
            }
            
            return x * 2;
        }
        catch (Exception ex)
        {
            // Writing to console is impure
            Console.WriteLine(ex.Message);
            return 0;
        }
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(10, 16, 10, 26)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }


    [Test]
    public async Task ThrowIfNull_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:Check|}(object o)
    {
        if (o == null) throw new ArgumentNullException(nameof(o));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ExceptionToString_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(Exception ex)
    {
        return ex.ToString();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}