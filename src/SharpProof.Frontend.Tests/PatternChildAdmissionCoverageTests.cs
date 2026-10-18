using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class PatternChildAdmissionCoverageTests
{
    [Test]
    public void ActualPatternInputKeepsUnboxingClosureAndIntegerReplayControl()
    {
        using var rejected = TypedProgramSubject.Create("bool Target(object value) => (int)value is >= 0;");
        Assert.That(rejected.Invoke([0]), Is.True);
        var tree = rejected.Compilation.SyntaxTrees.Single();
        var pattern = tree.GetRoot().DescendantNodes().OfType<IsPatternExpressionSyntax>().Single();
        var operation = rejected.Compilation.GetSemanticModel(tree).GetOperation(pattern)!;
        var builder = new IrProgramBuilder(rejected.Factory);
        var entry = builder.CreateBlock("entry");
        builder.SetEntry(entry);
        var child = new RoslynTotalExpressionLowerer(rejected.Context, builder).LowerBodyValue(operation, entry);
        Assert.That(child.Classification.IsExact, Is.False);
        Assert.That(child.Classification.Abstention, Is.EqualTo(FrontendAbstention.ConversionMayChangeValue));
        var rejectedProgram = rejected.Lower();
        Assert.That(rejectedProgram.IsExact, Is.False);
        Assert.That(rejectedProgram.ConstructionLimitExceeded, Is.False);

        using var supported = TypedProgramSubject.Create("bool Target(int value) => value is >= 0;");
        Assert.That(supported.Invoke([0]), Is.True);
        var lowering = supported.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = supported.Execute(lowering, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Boolean, Is.True);
    }
}
