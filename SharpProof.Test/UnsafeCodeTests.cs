using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class UnsafeCodeTests
{
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


        await VerifyUnsafeAsync(
            test,
            VerifyCS.Diagnostic("SP0002")
                .WithSpan("/0/Test1.cs", 8, 24, 8, 34)
                .WithArguments("TestMethod"));
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


        await VerifyUnsafeAsync(
            test,
            VerifyCS.Diagnostic("SP0002")
                .WithSpan("/0/Test1.cs", 8, 31, 8, 41)
                .WithArguments("TestMethod"));
    }

    private static Task VerifyUnsafeAsync(string source, DiagnosticResult expected)
    {
        var test = new VerifyCS.Test
        {
            TestState =
            {
                Sources = { MathAndAttributeTestSources.MinimalEnforcePureAttribute, source },
                ExpectedDiagnostics = { expected }
            }
        };
        test.SolutionTransforms.Add(EnableUnsafe);
        return test.RunAsync();
    }

    private static Solution EnableUnsafe(Solution solution, ProjectId projectId)
    {
        var project = solution.GetProject(projectId);
        if (project == null) return solution;

        var parseOptions = (project.ParseOptions as CSharpParseOptions)?
            .WithLanguageVersion(LanguageVersion.Latest);
        solution = solution.WithProjectParseOptions(
            projectId,
            parseOptions ?? project.ParseOptions ?? new CSharpParseOptions());

        var compilationOptions = (project.CompilationOptions as CSharpCompilationOptions)?
            .WithAllowUnsafe(true);
        return solution.WithProjectCompilationOptions(
            projectId,
            compilationOptions ?? project.CompilationOptions ??
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
