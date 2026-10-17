using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryCollectionInitializerCensusWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualInitializerAndElementCallsRetainTheirCompilerSourceIdentity(bool initializer)
    {
        var expression = initializer ? "new Items { new Items().Add(1) }.Count" : "new Items().Add(1)";
        var compilation = AnalyzerGateHost.CreateCompilation("""
            public sealed class Items : System.Collections.IEnumerable {
                public int Count => 1;
                public int Add([SharpProof.Attributes.Positive] int value) => value;
                public System.Collections.IEnumerator GetEnumerator() => System.Array.Empty<int>().GetEnumerator();
            }
            public static class Caller {
                public static int Root() =>
            """ + expression + "; }");
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        var calls = inventory.Owners.Single(owner => owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (owner.Method.Name == "Root")).Calls;
        Assert.That(calls, Has.Length.EqualTo(initializer ? 2 : 1));
        if (initializer)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(calls[0].Target, calls[1].Target), Is.True);
            Assert.That(calls[0].Syntax.SyntaxTree, Is.SameAs(calls[1].Syntax.SyntaxTree));
            Assert.That(calls[0].Syntax.Span, Is.EqualTo(calls[1].Syntax.Span));
        }

        var coverage = CorpusGate.MatchNativeContractCalls(compilation, []);

        Assert.That(coverage.Expected, Is.EqualTo(1));
        Assert.That(coverage.Failures.Count(failure => failure is null ? throw new System.ArgumentNullException(nameof(failure)) : (failure.StartsWith("DuplicateSourceObligation:", StringComparison.Ordinal))),
            Is.EqualTo(initializer ? 1 : 0));
    }
}
