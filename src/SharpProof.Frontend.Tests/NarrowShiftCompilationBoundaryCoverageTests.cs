using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NarrowShiftCompilationBoundaryCoverageTests
{
    [Test]
    public void ActualNarrowShiftRequiresDefaultOverflowContextAndKeepsCompiledReplay()
    {
        using var ordinary = TypedProgramSubject.Create("byte Target(byte value) => value <<= 1;");
        Assert.That(ordinary.Invoke([(byte)255]), Is.EqualTo((byte)254));
        var tree = ordinary.Compilation.SyntaxTrees.Single();
        var assignment = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var operation = ordinary.Compilation.GetSemanticModel(tree).GetOperation(assignment)!;
        var builder = new IrProgramBuilder(ordinary.Factory);
        var entry = builder.CreateBlock("entry");
        builder.SetEntry(entry);
        var child = new RoslynTotalExpressionLowerer(ordinary.Context, builder).LowerBodyValue(operation, entry);
        Assert.That(child.Classification.IsExact, Is.False);
        Assert.That(child.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        var lowering = ordinary.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var execution = ordinary.Execute(lowering, [(byte)255]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(254)));

        using var explicitChecked = TypedProgramSubject.Create("byte Target(byte value) => checked(value <<= 1);");
        Assert.That(explicitChecked.Invoke([(byte)2]), Is.EqualTo((byte)4));
        Assert.That(explicitChecked.Invoke([(byte)255]), Is.TypeOf<OverflowException>());
        var checkedLowering = explicitChecked.Lower();
        Assert.That(checkedLowering.IsExact, Is.True);
        Assert.That(checkedLowering.ConstructionLimitExceeded, Is.False);
        var checkedExecution = explicitChecked.Execute(checkedLowering, [(byte)2]);
        Assert.That(checkedExecution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(checkedExecution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(4)));
        var checkedOverflow = explicitChecked.Execute(checkedLowering, [(byte)255]);
        Assert.That(checkedOverflow.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(checkedOverflow.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
    }
}
