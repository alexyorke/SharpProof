using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceBodyCfgApiCoverageTests
{
    [Test]
    public void CompilerCfgBodyNodeRejectionDoesNotChangeTheOwningMethodWorkflow()
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) { return value + 1; }");
        Assert.That(subject.Invoke([17]), Is.EqualTo(18));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax.Identifier.ValueText == "Target");
        Assert.That(method.Body, Is.Not.Null);
        // A method body operation has a parent. Roslyn requires its declaration
        // as the root of this CFG API; both inputs come from the same real method.
        Assert.That(model.GetOperation(method.Body!)!.Parent, Is.Not.Null);
        Assert.That((Action)(() => { ControlFlowGraph.Create(method.Body!, model); }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(TotalSourceCallSession.CreateGraph(method.Body!, subject.Context.Target, model, default),
            Is.Null);
        Assert.That(TotalSourceCallSession.CreateGraph(method, subject.Context.Target, model, default),
            Is.Not.Null);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var execution = subject.Execute(lowered, [17]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(18)));
    }
}
