using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test;

[TestFixture]
public sealed class ExceptionContractTests
{
    [Test]
    public async Task DoesNotThrow_NoEscapingExceptions_NoDiagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [DoesNotThrow]
    public void Run()
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DoesNotThrow_DirectThrow_ReportsSp0030()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [DoesNotThrow]
    public void Run()
    {
        throw new InvalidOperationException();
    }
}");

        var diagnostic = SingleDiagnostic(diagnostics, "SP0030");
        Assert.That(diagnostic.GetMessage(), Does.Contain("[DoesNotThrow]"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("System.InvalidOperationException"));
        Assert.That(
            diagnostic.Properties["sharpproof.exception_contract.disallowed_types"],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task AllowedExceptions_AllowsDeclaredException_NoDiagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedExceptions(typeof(InvalidOperationException))]
    public void Run()
    {
        throw new InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AllowedExceptions_RejectsDisallowedException_ReportsSp0030()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedExceptions(typeof(ArgumentException))]
    public void Run()
    {
        throw new InvalidOperationException();
    }
}");

        var diagnostic = SingleDiagnostic(diagnostics, "SP0030");
        Assert.That(diagnostic.GetMessage(), Does.Contain("[AllowedExceptions]"));
        Assert.That(
            diagnostic.Properties["sharpproof.exception_contract.allowed_types"],
            Is.EqualTo("System.ArgumentException"));
        Assert.That(
            diagnostic.Properties["sharpproof.exception_contract.disallowed_types"],
            Is.EqualTo("System.InvalidOperationException"));
    }

    [Test]
    public async Task AllowedExceptions_AllowsDerivedExceptionWhenBaseIsAllowed()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedExceptions(typeof(Exception))]
    public void Run()
    {
        throw new InvalidOperationException();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AllowedExceptions_NonExceptionType_ReportsInvalidContractArgument()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedExceptions({|SP0024:typeof(string)|})]
    public void Run()
    {
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DoesNotThrow_OnProperty_AliasesGetter()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [DoesNotThrow]
    public int Value => 42;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AllowedExceptions_OnProperty_AliasesGetter()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [AllowedExceptions(typeof(Exception))]
    public int Value => 42;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
