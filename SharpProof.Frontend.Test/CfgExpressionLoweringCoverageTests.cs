using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CfgExpressionLoweringCoverageTests
{
    [TestCase("string Target(string text) => text ?? \"fallback\";")]
    [TestCase("long Target(bool flag, long left, long right) => flag ? left : right;")]
    public void ActualCfgCapturesAndNullTestsRemainExact(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var operations = subject.Graph.Blocks.SelectMany(block => block.Operations
            .Concat(block.BranchValue == null ? Array.Empty<IOperation>() : [block.BranchValue])).ToArray();
        Assert.That(operations.OfType<IFlowCaptureOperation>(), Is.Not.Empty);
        foreach (var operation in operations)
        {
            var lowered = lowerer.Lower(operation);
            Assert.That(lowered.IsExact, Is.True, operation.Kind + ": " + operation.Syntax);
            Assert.That(lowered.Term, Is.Not.Null);
        }
        var captures = lowerer.CreateCaptureBindings();
        Assert.That(captures, Is.Not.Empty);
        Assert.That(captures, Is.Unique);
    }

    [Test]
    public void InvalidCompilerExpressionRetainsInvalidOperationAbstention()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "public static class Subject { public static int Target() => Missing(); }");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("InvalidBinding", [tree],
            TestMetadataReferences.Platform,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var syntax = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.Kind, Is.EqualTo(OperationKind.Invalid));
        var result = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.InvalidOperation));
    }
}
