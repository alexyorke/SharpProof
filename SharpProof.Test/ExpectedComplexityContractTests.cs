using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Symbolic;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class ExpectedComplexityContractTests
{
    private static readonly ImmutableArray<MetadataReference> ComplexityFrameworkReferences =
        GetMinimalFrameworkReferences();

    [Test]
    public async Task ExpectedComplexity_ConstantMethod_Passes()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Constant)]
    public static int Work(int value)
    {
        return value + 1;
    }
}";

        await AssertNoComplexityDiagnosticsAsync(test);
    }

    [Test]
    public async Task ExpectedComplexity_LinearMethod_Passes()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int Work(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += i;
        }

        return sum;
    }
}";

        await AssertNoComplexityDiagnosticsAsync(test);
    }

    [Test]
    public async Task ExpectedComplexity_QuadraticMethodAgainstLinear_ReportsSp0021()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int Work(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.ComplexityExceededId);
        Assert.That(diagnostic.GetMessage(), Does.Contain("O(n^2)"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("O(n)"));
    }

    [Test]
    public async Task ExpectedComplexity_UnsupportedWhileLoop_ReportsSp0022()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    public static int Step(int value) => value + 1;

    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int Work(int n)
    {
        var i = 0;
        while (i < n)
        {
            i = Step(i);
        }

        return i;
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.GetMessage(), Does.Contain("UnsupportedWhileLoop"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.UnknownReasonCodeProperty],
                Is.EqualTo("complexity.unsupported_while_loop"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.UnknownReasonCategoryProperty],
                Is.EqualTo(SymbolicUnknownReasonCategory.UnsupportedSyntax.ToString()));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.UnknownReasonSourceProperty],
                Is.EqualTo(SymbolicUnknownReasonSource.Complexity.ToString()));
        });
    }

    [Test]
    public async Task ExpectedComplexity_ProductAgainstQuadratic_RemainsConservative()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Quadratic)]
    public static int Work(int n, int m)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId);
        Assert.That(diagnostic.GetMessage(), Does.Contain("not directly comparable"));
    }

    [Test]
    public async Task ExpectedComplexity_LinearMethodAgainstLinearithmic_Passes()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Linearithmic)]
    public static int Work(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += i;
        }

        return sum;
    }
}";

        await AssertNoComplexityDiagnosticsAsync(test);
    }

    [Test]
    public async Task ExpectedComplexity_ConstantMethodAgainstLogarithmic_Passes()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Logarithmic)]
    public static int Work(int value)
    {
        return value + 1;
    }
}";

        await AssertNoComplexityDiagnosticsAsync(test);
    }

    [Test]
    public async Task ExpectedComplexity_LinearMethodAgainstLogarithmic_ReportsSp0021()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Logarithmic)]
    public static int Work(int n)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += i;
        }

        return sum;
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.ComplexityExceededId);
        Assert.That(diagnostic.GetMessage(), Does.Contain("O(n)"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("O(log n)"));
    }

    [Test]
    public async Task ExpectedComplexity_ProductMethodAgainstProduct_Passes()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Product)]
    public static int Work(int n, int m)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}";

        await AssertNoComplexityDiagnosticsAsync(test);
    }

    [Test]
    public async Task ExpectedComplexity_MaxMethodAgainstMax_Passes()
    {
        // Two sequential loops over independent parameters yield O(max(n, m)).
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Max)]
    public static int Work(int n, int m)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += i;
        }

        for (var j = 0; j < m; j++)
        {
            sum += j;
        }

        return sum;
    }
}";

        await AssertNoComplexityDiagnosticsAsync(test);
    }

    [Test]
    public async Task ExpectedComplexity_ProductMethodAgainstMax_RemainsConservative()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Max)]
    public static int Work(int n, int m)
    {
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                sum += i + j;
            }
        }

        return sum;
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId);
        Assert.That(diagnostic.GetMessage(), Does.Contain("not directly comparable"));
    }

    [Test]
    public async Task ExpectedComplexity_ExternalCallee_ReportsSp0022()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity(ComplexityKind.Linear)]
    public static int Work(int n)
    {
        _ = Environment.GetEnvironmentVariable(""PATH"");
        return n;
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId);
        Assert.That(
            diagnostic.GetMessage(),
            Does.Contain("ExternalCallee").Or.Contain("UnknownCallee"));
    }

    [Test]
    public async Task ExpectedComplexity_OpenVirtualSourceCallee_ReportsSp0022()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public class Worker
{
    public virtual void Work(int n)
    {
    }
}

public sealed class LinearWorker : Worker
{
    public override void Work(int n)
    {
        for (var i = 0; i < n; i++)
        {
        }
    }
}

public static class C
{
    [ExpectedComplexity(ComplexityKind.Constant)]
    public static void Caller(Worker worker, int n)
    {
        worker.Work(n);
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId);
        Assert.That(diagnostic.GetMessage(), Does.Contain("DynamicDispatch"));
    }

    [Test]
    public async Task ExpectedComplexity_InvalidEnumValue_ReportsSp0024()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class C
{
    [ExpectedComplexity((ComplexityKind)99)]
    public static int Work(int value)
    {
        return value + 1;
    }
}";

        var diagnostics = await GetComplexityDiagnosticsAsync(test);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.InvalidContractArgumentId);
        Assert.That(diagnostic.GetMessage(), Does.Contain("undefined ComplexityKind value").And.Contain("99"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ContractAttributeProperty],
            Is.EqualTo("[ExpectedComplexity]"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ContractArgumentProperty], Is.EqualTo("99"));
    }

    [Test]
    public async Task ExpectedComplexity_OnProperty_AliasesGetter()
    {
        var test = CreateExpressionBodiedPropertyContractSource(
            "ExpectedComplexity(ComplexityKind.Constant)",
            disablePurityPlacementDiagnostic: true);

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    private static Task<ImmutableArray<Diagnostic>> GetComplexityDiagnosticsAsync(string source)
    {
        return GetDiagnosticsAsync(
            source,
            frameworkReferences: ComplexityFrameworkReferences,
            concurrentAnalysis: true);
    }

    private static async Task AssertNoComplexityDiagnosticsAsync(string source)
    {
        var diagnostics = await GetComplexityDiagnosticsAsync(source);
        Assert.That(diagnostics, Is.Empty);
    }
}
