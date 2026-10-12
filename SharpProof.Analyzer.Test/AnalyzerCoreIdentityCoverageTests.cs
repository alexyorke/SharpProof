using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreIdentityCoverageTests
{
    [Test]
    public async Task AllowedBaseExceptionIncludesNestedGenericArrayArgumentIdentity()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public class Outer<T> {
                public sealed class Failure<U> : System.Exception { }
            }
            public static class Fixture {
                [AllowedExceptions(typeof(System.Exception))]
                public static int Target() => throw new Outer<int>.Failure<string[]>();
            }
            """, "effects", ["SP0046"], new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public void OperationAncestorsFollowTheSemanticParentChain()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Fixture { public static int Target(int x) { return x + 1; } }", []);
        var tree = compilation.SyntaxTrees.Single();
        var binary = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(binary)!;
        var ancestors = OperationAncestors.Of(operation).ToArray();
        Assert.That(ancestors, Is.Not.Empty);
        Assert.That(ancestors[0], Is.SameAs(operation.Parent));
        for (var index = 1; index < ancestors.Length; index++)
        {
            Assert.That(ancestors[index], Is.SameAs(ancestors[index - 1].Parent));
        }
        Assert.That(ancestors[^1].Parent, Is.Null);
        Assert.That(OperationAncestors.Of(ancestors[^1]), Is.Empty);
    }
}
