using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ControlFlowGeneratorCompileRegressionTests
{
    [TestCase(251008)]
    [TestCase(251009)]
    [TestCase(251010)]
    [TestCase(251155)]
    [TestCase(251248)]
    [TestCase(251258)]
    [TestCase(251414)]
    [TestCase(251474)]
    public void GeneratedSeedCompiles(int seed)
    {
        var compilation = ControlFlowLongFuzzTests.CompileCandidateForSeed(seed);
        var source = compilation.SyntaxTrees.Single().ToString();
        if (seed == 251009)
        {
            Assert.That(source, Does.Contain("if (x == 3) break;"));
            Assert.That(source, Does.Contain("catch { x = 2; }"));
        }
        if (seed == 251155)
        {
            Assert.That(source, Does.Contain("foreach (var c in \"ab\") { x += c; if (x == 3) continue; }"));
        }
        if (seed == 251248)
        {
            Assert.That(source, Does.Contain("case > 3: for (int __flowExit = 0;"));
            Assert.That(source, Does.Contain("if (x == 3) continue; x++; }"));
        }
        if (seed == 251258)
        {
            Assert.That(source, Does.Contain("int L1() {"));
            Assert.That(source.IndexOf("int L1() {", StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("for (int __flowExit = 0;", StringComparison.Ordinal)));
        }
        if (seed == 251414)
        {
            Assert.That(source, Does.Contain("if (x == 3) break; break;"));
            Assert.That(source, Does.Not.Contain("__flowExit"));
        }
        if (seed == 251474)
        {
            Assert.That(source, Does.Contain("for (int i = 0; i < 2; i++) { if (x == 3) break; }"));
        }

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .ToImmutableArray();
        Assert.That(errors, Is.Empty,
            $"seed {seed}: {string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString())))}\n{source}");
    }
}
