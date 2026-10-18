using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FieldMutationMaterializationCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PostfixValueRemainsMaterializedBeforeTheFollowingRead(bool property)
    {
        var declaration = property ? "public int Value { get; set; }" : "public int Value;";
        using var subject = TypedProgramSubject.Create(
            "int Target() { var cell = new Cell(); cell.Value = 17; return cell.Value++ + cell.Value; }",
            "public sealed class Cell { " + declaration + " }");
        Assert.That(subject.Invoke([]), Is.EqualTo(35));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().Single();
        var operation = (IIncrementOrDecrementOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.IsPostfix, Is.True);
        Assert.That(operation.OperatorMethod, Is.Null);
        Assert.That(operation.IsLifted, Is.False);
        Assert.That(operation.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        if (property)
        {
            Assert.That(operation.Target, Is.InstanceOf<IPropertyReferenceOperation>());
            Assert.That(((IPropertyReferenceOperation)operation.Target).Instance, Is.InstanceOf<ILocalReferenceOperation>());
        }
        else
        {
            Assert.That(operation.Target, Is.InstanceOf<IFieldReferenceOperation>());
            Assert.That(((IFieldReferenceOperation)operation.Target).Instance, Is.InstanceOf<ILocalReferenceOperation>());
        }
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(35)));
    }

    [Test]
    public void NullFieldReceiverRetainsItsActualClrAndIrFault()
    {
        using var subject = TypedProgramSubject.Create("int Target(Cell cell) => cell.Value++;",
            "public sealed class Cell { public int Value; }");
        Assert.That(subject.Invoke([null!]), Is.InstanceOf<NullReferenceException>());
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var parameter = subject.Context.Parameters.Single().Entry;
        var type = subject.Factory.GetVariableInfo(parameter).Type;
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program,
            new Dictionary<IrVarId, IrValue> { [parameter] = subject.Factory.CreateNullValue(type) });
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.NullReference));
        Assert.That(replay.ConsumedApproximation, Is.False);
    }
}
