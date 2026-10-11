using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ContractClaimUnaryMinCompileRegressionTests
{
    [TestCase(360)]
    [TestCase(361)]
    public void GeneratedUnaryMinimumValueCompiles(int seed)
    {
        var (source, _, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        if (seed == 361)
        {
            Assert.That(source, Does.Contain("(-int.MinValue)"));
        }

        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: []),
            "/project/Subject.cs", Encoding.UTF8);
        var compilation = CSharpCompilation.Create("UnaryMinCompile" + seed, [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Is.Empty,
            $"seed {seed}: {string.Join(" | ", errors.Select(diagnostic => diagnostic.ToString()))}\n{source}");
    }

    [Test]
    public void ExplicitCheckedConstantOverflowRemainsInvalid()
    {
        const string source = "public static class Subject { public static int M() => checked(int.MaxValue + 1); }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: []),
            "/project/Subject.cs", Encoding.UTF8);
        var compilation = CSharpCompilation.Create("CheckedOverflowControl", [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors.Select(diagnostic => diagnostic.Id), Does.Contain("CS0220"));
    }
}
