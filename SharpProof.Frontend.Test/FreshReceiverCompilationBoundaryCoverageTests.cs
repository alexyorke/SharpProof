using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FreshReceiverCompilationBoundaryCoverageTests
{
    [TestCase("source", true)]
    [TestCase("missing", false)]
    [TestCase("different", false)]
    public void ParenthesizedFreshReceiverRequiresItsActualCompilation(string mode, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText("public sealed class Box { public int Value; } public static class Subject { public static int Target() { ((new Box())).Value = 17; return 17; } }");
        var compilation = CSharpCompilation.Create("FreshReceiverBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var receiver = tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>().Single().Expression;
        Compilation? selected = mode switch
        {
            "source" => compilation,
            "missing" => null,
            _ => CSharpCompilation.Create("DifferentReceiverBoundary", [CSharpSyntaxTree.ParseText("public sealed class Other { }")],
                TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
        };
        Assert.That(CSharpOperationSemantics.IsFreshReceiver(receiver, selected), Is.EqualTo(expected));
    }
}
