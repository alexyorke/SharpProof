using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ContractClaimGeneratorEnsuresEqualityRegressionTests
{
    [TestCase(251026)]
    [TestCase(251027)]
    [TestCase(251028)]
    public void GeneratedProgramCompilesAtEqualitySeedAndNeighbors(int seed)
    {
        var (source, _, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: []),
            "/project/Subject.cs", Encoding.UTF8);
        var compilation = CSharpCompilation.Create("LongFuzzCompile" + seed, [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Is.Empty,
            $"seed {seed}: {string.Join(" | ", errors.Select(diagnostic => diagnostic.ToString()))}\n{source}");
    }
}
