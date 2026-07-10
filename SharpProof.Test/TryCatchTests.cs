using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class TryCatchTests
{
    [Test]
    public async Task PureTryCatch_NoDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int PureMethod()
    {
        try
        {
            int x = 1;
            int y = 0;
            return x / y;
        }
        catch (System.Exception)
        {
            return 0;
        }
        finally
        {
            int z = 1;
        }
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task ImpureTryBody_ReportsDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public class TestClass
{
    private int _val = 0;

    [EnforcePure]
    public int ImpureMethod()
    {
        try
        {
            _val = 1; // Impure
            return 1;
        }
        catch
        {
            return 0;
        }
    }
}";
        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(9, 16, 9, 28)
            .WithArguments("ImpureMethod");
        await VerifyCS.VerifyAnalyzerAsync(code, expected);
    }

    [Test]
    public async Task ImpureCatchBody_ReportsDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public class TestClass
{
    private int _val = 0;

    [EnforcePure]
    public int ImpureMethod()
    {
        try
        {
            return 1;
        }
        catch
        {
            _val = 1; // Impure
            return 0;
        }
    }
}";
        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(9, 16, 9, 28)
            .WithArguments("ImpureMethod");
        await VerifyCS.VerifyAnalyzerAsync(code, expected);
    }

    [Test]
    public async Task ImpureCatchFilter_ReportsDiagnostic()
    {
        var code = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:ImpureMethod|}(int divisor)
    {
        try
        {
            return 1 / divisor;
        }
        catch (Exception) when (Console.Read() > 0)
        {
            return 0;
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code);
    }
}