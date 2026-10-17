using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class UserOperatorFieldIncrementApiCoverageTests
{
    [Test]
    public void ActualReferenceFieldOperatorIncrementRetainsConservativeAdmission()
    {
        const string members = "int Target() => Helper(new Cell { Value = new Counter { Count = 1 } }); " +
            "public static int Helper(Cell cell) { cell.Value++; return cell.Value.Count; }";
        const string types = "public sealed class Cell { public Counter Value; } " +
            "public sealed class Counter { public int Count; public static Counter operator ++(Counter value) " +
            "=> new Counter { Count = value.Count + 1 }; }";
        using var subject = TypedProgramSubject.Create(members, types);
        Assert.That(subject.Invoke([]), Is.EqualTo(2));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var helper = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(s => s.Identifier.ValueText == "Helper");
        var syntax = helper.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>().Single();
        var operation = model.GetOperation(syntax);
        Assert.That(operation, Is.InstanceOf<IIncrementOrDecrementOperation>());
        var increment = (IIncrementOrDecrementOperation)operation!;
        Assert.That(increment.Kind, Is.EqualTo(OperationKind.Increment));
        Assert.That(increment.IsPostfix, Is.True);
        Assert.That(increment.IsLifted, Is.False);
        Assert.That(increment.OperatorMethod, Is.Not.Null);
        Assert.That(increment.OperatorMethod!.Name, Is.EqualTo("op_Increment"));
        Assert.That(increment.OperatorMethod.MethodKind, Is.EqualTo(MethodKind.UserDefinedOperator));
        Assert.That(increment.OperatorMethod.IsStatic, Is.True);
        Assert.That(increment.OperatorMethod.Parameters, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(increment.OperatorMethod.ContainingAssembly, subject.Compilation.Assembly), Is.True);
        Assert.That(increment.Type!.IsReferenceType, Is.True);
        Assert.That(increment.Target, Is.InstanceOf<IFieldReferenceOperation>());
        var field = (IFieldReferenceOperation)increment.Target;
        Assert.That(field.Field.IsStatic, Is.False);
        Assert.That(field.Instance, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(SymbolEqualityComparer.Default.Equals(field.Field.Type, increment.Type), Is.True);
        Assert.That(CSharpOperationSemantics.IsSupportedFieldRead(field.Field), Is.True);
        Assert.That(CSharpOperationSemantics.IsSupportedFieldWrite(field.Field, subject.Compilation.Assembly), Is.True);
        var context = new TotalLoweringContext(subject.Factory, (IMethodSymbol)model.GetDeclaredSymbol(helper)!);
        Assert.That(context.OwnsParameter(((IParameterReferenceOperation)field.Instance!).Parameter), Is.True);
        var graph = ControlFlowGraph.Create(helper, model)!;
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        var builder = new IrProgramBuilder(subject.Factory);
        var entry = builder.CreateBlock("reference-increment-api");
        var exit = builder.CreateBlock("exceptional-exit");
        builder.ExceptionalExit(exit, subject.Factory.CreateOperation("api:exceptional-exit"));
        var body = new RoslynTotalExpressionLowerer(context, builder, exit).LowerBodyValue(increment, entry);
        Assert.That(body.Classification.IsExact, Is.False);
        Assert.That(body.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(body.Value, Is.InstanceOf<IrVariableTerm>());
        Assert.That(body.Value.Type, Is.EqualTo(context.Type(increment.Type)));
        var wholeHelper = new RoslynProgramLowerer(subject.Factory).LowerCandidate(graph, context);
        Assert.That(wholeHelper.IsExact, Is.False);
        Assert.That(wholeHelper.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(wholeHelper.ConstructionLimitExceeded, Is.False);
        // Neither incomplete result is replayed; the actual CLR result observed above.
        using var control = TypedProgramSubject.Create("int Target(int value) => value + 1;");
        Assert.That(control.Invoke([17]), Is.EqualTo(18));
        var exact = control.Lower();
        Assert.That(exact.IsExact, Is.True);
        Assert.That(exact.ConstructionLimitExceeded, Is.False);
        var replay = control.Execute(exact, [17]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(18)));
    }
}
