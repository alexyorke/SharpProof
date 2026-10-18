using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ShiftPureOperatorDomainCoverageTests
{
    [Test]
    public void PureShiftRejectsActualAdditionOperator()
    {
        using var subject = TypedProgramSubject.Create("int Target(int value, int count) => value + count;");
        Assert.That(subject.Invoke([7, 3]), Is.EqualTo(10));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = (IBinaryOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        var left = lowerer.LowerClause(operation.LeftOperand);
        var right = lowerer.LowerClause(operation.RightOperand);
        Assert.That(left.Classification.IsExact, Is.True);
        Assert.That(right.Classification.IsExact, Is.True);
        var rule = CSharpOperationSemantics.Shift(subject.Factory, operation.OperatorKind, left.Value, right.Value);
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(subject.Factory).Evaluate(rule.Value, new Dictionary<IrVarId, IrValue>()).Value!.Boolean, Is.False);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, [7, 3]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(10)));
    }
}
