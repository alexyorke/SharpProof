using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class UnsafeCodeTests
{
    private const string MinimalEnforcePureAttributeSource = @"
using System;

namespace SharpProof.Attributes
{
    [AttributeUsage(AttributeTargets.All)] // Use permissive usage for testing
    public sealed class EnforcePureAttribute : Attribute { }
}
";

    [Test]
    public async Task MethodWithUnsafeCode_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public unsafe void TestMethod()
    {
        int x = 5;
        int* p = &x;
        *p = 10; // Unsafe code is considered impure
    }
}
";


        var expected = new[]
        {
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                .WithSpan("/0/Test1.cs", 8, 24, 8, 34)
                .WithArguments("TestMethod")
        };

        var verifierTest = new VerifyCS.Test
        {
            TestState =
            {
                Sources = { MinimalEnforcePureAttributeSource, test },
                ExpectedDiagnostics = { expected[0] }
            }
        };


        verifierTest.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId);
            if (project == null) return solution;

            var parseOptions = (project.ParseOptions as CSharpParseOptions)?
                .WithLanguageVersion(LanguageVersion.Latest);

            solution = solution.WithProjectParseOptions(projectId,
                parseOptions ?? project.ParseOptions ?? new CSharpParseOptions());

            var compilationOptions = (project.CompilationOptions as CSharpCompilationOptions)?
                .WithAllowUnsafe(true);

            solution = solution.WithProjectCompilationOptions(projectId,
                compilationOptions ?? project.CompilationOptions ??
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return solution;
        });
        await verifierTest.RunAsync();
    }

    [Test]
    public async Task MethodWithFixedStatement_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public static unsafe void TestMethod()
    {
        byte[] array = new byte[10];
        fixed (byte* ptr = array)
        {
            *ptr = 42;
        }
    }
}
";


        var expected = new[]
        {
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                .WithSpan("/0/Test1.cs", 8, 31, 8, 41)
                .WithArguments("TestMethod")
        };

        var verifierTest = new VerifyCS.Test
        {
            TestState =
            {
                Sources = { MinimalEnforcePureAttributeSource, test },
                ExpectedDiagnostics = { expected[0] }
            }
        };


        verifierTest.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId);
            if (project == null) return solution;

            var parseOptions = (project.ParseOptions as CSharpParseOptions)?
                .WithLanguageVersion(LanguageVersion.Latest);

            solution = solution.WithProjectParseOptions(projectId,
                parseOptions ?? project.ParseOptions ?? new CSharpParseOptions());

            var compilationOptions = (project.CompilationOptions as CSharpCompilationOptions)?
                .WithAllowUnsafe(true);

            solution = solution.WithProjectCompilationOptions(projectId,
                compilationOptions ?? project.CompilationOptions ??
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return solution;
        });

        await verifierTest.RunAsync();
    }
}