using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerOrdinaryParameterAttributeSelectionTests
{
    [Test]
    public void RealBclAllowNullAnnotationDoesNotBecomeAClosedContractOrDiscardTheBody()
    {
        using var subject = new MetadataTestSubject(
            "using System.Diagnostics.CodeAnalysis; public static class Library { " +
            "public static int Target([AllowNull] int value) => value; }",
            "public static class Subject { public static int Target(int value) => Library.Target(value); }");
        var tree = subject.Compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = (IInvocationOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(invocation)!;
        var method = operation.TargetMethod;
        Assert.That(method.Parameters.Single().GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.AllowNullAttribute"), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, subject.Compilation.Assembly), Is.False);
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        Assert.Throws<OperationCanceledException>((Action)(() =>
            provider.Resolve(method, new CancellationToken(true))));
        var decoded = provider.Resolve(method, CancellationToken.None);
        Assert.That(decoded, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(decoded!.Method, method), Is.True);
        Assert.That(decoded.ParameterAttributes, Is.Empty);
        Assert.That(decoded.Instructions.Last().Code, Is.EqualTo("ret"));
        foreach (var value in new[] { -2, 0, 7 })
        {
            Assert.That(subject.Method.Invoke(null, [value]), Is.EqualTo(value));
            Assert.That(subject.RootMethod.Invoke(null, [value]), Is.EqualTo(value));
        }
    }
}
