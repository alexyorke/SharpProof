using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using System.Text;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ContractClaimGeneratorCompileRegressionTests
{
    [TestCase(11)]
    public void ReturnInsideGeneratedFinallyIsAvoided(int seed)
    {
        var (source, _, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        Assert.That(source, Does.Contain("finally {"));
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12,
            preprocessorSymbols: []), "/project/Subject.cs", Encoding.UTF8);
        var compilation = CSharpCompilation.Create("LongFuzzCompile" + seed, [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Is.Empty, $"seed {seed}: {string.Join(" | ", errors.Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.ToString())))}\n{source}");
        Assert.That(source, Does.Contain("if (y == State) _ = (s == null ? -1 : s.Length);"));
        Assert.That(source, Does.Contain("if (a != null) return (v3 % State);"));
        var finallyClauses = tree.GetRoot().DescendantNodes().OfType<FinallyClauseSyntax>().ToImmutableArray();
        Assert.That(finallyClauses, Is.Not.Empty);
        Assert.That(finallyClauses.SelectMany(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Block.DescendantNodes().OfType<ReturnStatementSyntax>())),
            Is.Empty);
    }
}
