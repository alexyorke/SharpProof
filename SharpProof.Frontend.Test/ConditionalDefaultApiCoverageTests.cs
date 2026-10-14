using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalDefaultApiCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualConditionalDefaultPreservesTheExpressionApisOwnedTypeBoundary(bool total)
    {
        using var subject = TypedProgramSubject.Create(
            "long Target(bool condition, long value) => condition ? value : default(long);");
        Assert.That(subject.Invoke([false, 7L]), Is.EqualTo(0L));
        Assert.That(subject.Invoke([true, 7L]), Is.EqualTo(7L));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(operation, Is.InstanceOf<IConditionalOperation>());
        var conditional = (IConditionalOperation)operation;
        Assert.That(conditional.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        Assert.That(conditional.Condition, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(conditional.WhenTrue, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(conditional.WhenFalse, Is.InstanceOf<IDefaultValueOperation>());
        Assert.That(conditional.WhenTrue.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        Assert.That(conditional.WhenFalse!.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        var factory = new IrFactory(total ? IrExecutionSemantics.Total : IrExecutionSemantics.Legacy);
        var lowerer = new RoslynOperationLowerer(factory);
        var condition = lowerer.Lower(conditional.Condition);
        var whenTrue = lowerer.Lower(conditional.WhenTrue);
        var whenFalse = lowerer.Lower(conditional.WhenFalse!);
        Assert.That(condition.IsExact, Is.True);
        Assert.That(whenTrue.IsExact, Is.True);
        Assert.That(whenFalse.IsExact, Is.True);
        Assert.That(condition.Term, Is.InstanceOf<IrVariableTerm>());
        Assert.That(condition.Term.Type, Is.EqualTo(factory.BooleanType));
        Assert.That(whenTrue.Term, Is.InstanceOf<IrVariableTerm>());
        Assert.That(whenFalse.Term, Is.InstanceOf<IrIntegerTerm>());
        Assert.That(((IrIntegerTerm)whenFalse.Term).Value, Is.Zero);
        Assert.That(whenFalse.Term.Type, Is.EqualTo(factory.IntegerType));
        Assert.That(whenTrue.Term.Type,
            Is.EqualTo(total ? factory.GetOrCreateIntegerType(64, true) : factory.IntegerType));
        if (total)
        {
            Assert.Throws<ArgumentException>(() => factory.Conditional(condition.Term, whenTrue.Term, whenFalse.Term));
        }
        var lowered = lowerer.Lower(conditional);
        Assert.That(lowered.IsExact, Is.EqualTo(!total));
        Assert.That(lowered.Classification.Abstention,
            Is.EqualTo(total ? FrontendAbstention.UnsupportedType : FrontendAbstention.None));
        if (total)
        {
            Assert.That(lowered.Term, Is.InstanceOf<IrOpaqueTerm>());
        }
        else
        {
            var conditionVariable = ((IrVariableTerm)condition.Term).Variable;
            var valueVariable = ((IrVariableTerm)whenTrue.Term).Variable;
            foreach (var choice in new[] { false, true })
            {
                var values = new Dictionary<IrVarId, IrValue>
                {
                    [conditionVariable] = factory.CreateBooleanValue(choice),
                    [valueVariable] = factory.CreateIntegerValue(factory.IntegerType, 7)
                };
                var evaluation = new IrInterpreter(factory).Evaluate(lowered.Term, values);
                Assert.That(evaluation.Value!.IntegerNumericValue, Is.EqualTo(new BigInteger(choice ? 7 : 0)));
            }
        }
        var program = subject.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        foreach (var choice in new[] { false, true })
        {
            var replay = subject.Execute(program, [choice, 7L]);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(choice ? 7 : 0)));
        }
    }
}
