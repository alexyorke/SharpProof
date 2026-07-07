using NUnit.Framework;
using SharpProof.Analyzer;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using static SharpProof.Test.AnalyzerTestHost;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
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
            Assert.That(diagnostic.GetMessage(), Does.Contain("UnsupportedWhileLoop"));
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
        public async Task ExpectedComplexity_OnProperty_ReportsSp0023()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0023:ExpectedComplexity(ComplexityKind.Constant)|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        private static Task<System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> GetComplexityDiagnosticsAsync(string source)
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
}
