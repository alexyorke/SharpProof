using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FieldIncrementApiDomainCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualFieldIncrementRetainsScalarAdmission(bool floating)
    {
        const string source = "int Target() { var cell = new Cell(); cell.Value = 4; cell.Value++; return (int)cell.Value; }";
        var cellSource = floating ? "public sealed class Cell { public float Value; }"
            : "public sealed class Cell { public int Value; }";
        using var subject = TypedProgramSubject.Create(source, cellSource);
        Assert.That(subject.Invoke([]), Is.EqualTo(5));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IIncrementOrDecrementOperation>());
        var increment = (IIncrementOrDecrementOperation)operation!;
        Assert.That(increment.OperatorMethod, Is.Null);
        Assert.That(increment.IsLifted, Is.False);
        Assert.That(increment.Target, Is.AssignableTo<IFieldReferenceOperation>());
        var field = (IFieldReferenceOperation)increment.Target;
        Assert.That(field.Field.Type.SpecialType,
            Is.EqualTo(floating ? SpecialType.System_Single : SpecialType.System_Int32));
        Assert.That(field.Instance, Is.AssignableTo<ILocalReferenceOperation>());
        Assert.That(CSharpOperationSemantics.IsSupportedFieldRead(field.Field), Is.True);
        Assert.That(CSharpOperationSemantics.IsSupportedFieldWrite(field.Field,
            subject.Compilation.Assembly), Is.EqualTo(!floating));
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock("entry");
        var exceptionalExit = builder.CreateBlock("exceptional-exit");
        builder.SetEntry(entry);
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context, builder, exceptionalExit)
        {
            AllowOpaqueCalls = true
        };
        var lowered = lowerer.LowerBodyValue(increment, entry);
        Assert.That(lowered.Classification.IsExact, Is.EqualTo(!floating));
        Assert.That(lowered.Classification.Abstention,
            Is.EqualTo(floating ? FrontendAbstention.UnsupportedOperationKind : FrontendAbstention.None));
        if (!floating)
        {
            var program = subject.LowerSourceCalls(false, opaqueCalls: true);
            Assert.That(program.IsExact, Is.True);
            Assert.That(program.ConstructionLimitExceeded, Is.False);
            var replay = subject.Execute(program, []);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(5)));
        }
    }
}
