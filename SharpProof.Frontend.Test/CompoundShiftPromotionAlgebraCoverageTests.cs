using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompoundShiftPromotionAlgebraCoverageTests
{
    [TestCase(8, true, 1L, 1, 2L)]
    [TestCase(8, false, 255L, 1, 510L)]
    [TestCase(32, true, 1L, -1, int.MinValue)]
    [TestCase(64, true, 1L, 65, 2L)]
    public void OwnedScalarPromotionPreservesWidthAndMaskedCount(
        int width, bool isSigned, long input, int count, long expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var valueVariable = factory.CreateVariable("value", type);
        var countVariable = factory.CreateVariable("count", factory.IntegerType);
        var rule = CSharpOperationSemantics.PromoteCompoundShift(factory, BinaryOperatorKind.LeftShift,
            factory.Variable(valueVariable), factory.Variable(countVariable));
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(rule.Value.Type, Is.EqualTo(width < 32 ? factory.IntegerType : type));
        var environment = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(valueVariable, factory.CreateIntegerValue(type, input)).SetItem(countVariable, factory.CreateIntegerValue(factory.IntegerType, count));
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value, environment).Value!.IntegerNumericValue,
            Is.EqualTo(new BigInteger(expected)));
    }

    [TestCase(32, false)]
    [TestCase(64, true)]
    [TestCase(16, true)]
    [TestCase(0, false)]
    public void OwnedScalarPromotionRejectsNonInt32SignedCount(int countWidth, bool isSigned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var countType = countWidth == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(countWidth, isSigned);
        var valueVariable = factory.CreateVariable("value", factory.IntegerType);
        var countVariable = factory.CreateVariable("count", countType);
        var rule = CSharpOperationSemantics.PromoteCompoundShift(factory, BinaryOperatorKind.LeftShift,
            factory.Variable(valueVariable), factory.Variable(countVariable));
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value).Value!.Boolean, Is.False);
    }

    [TestCase("sbyte Target(sbyte value, int count) { value <<= count; return value; }", (sbyte)64, 1, -128L)]
    [TestCase("int Target(int value, int count) { value <<= count; return value; }", 1, -1, int.MinValue)]
    [TestCase("long Target(long value, int count) { value <<= count; return value; }", 1L, 65, 2L)]
    public void ActualCompoundOperationKeepsMatchingOperandsAndNativeReplay(
        string source, object input, int count, long expected)
    {
        System.ArgumentNullException.ThrowIfNull(input);
        System.ArgumentNullException.ThrowIfNull(source);
        using var subject = TypedProgramSubject.Create(source);
        object[] inputs = [input, count];
        Assert.That(subject.Invoke(inputs), Is.EqualTo(input is sbyte ? (object)(sbyte)expected
            : input is long ? expected : (object)(int)expected));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var operation = (ICompoundAssignmentOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.OperatorKind, Is.EqualTo(BinaryOperatorKind.LeftShift));
        Assert.That(operation.IsLifted, Is.False);
        Assert.That(operation.OperatorMethod, Is.Null);
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        var left = lowerer.LowerClause(operation.Target);
        var right = lowerer.LowerClause(operation.Value);
        Assert.That(left.Classification.IsExact, Is.True);
        Assert.That(right.Classification.IsExact, Is.True);
        var rule = CSharpOperationSemantics.Compound(subject.Factory, operation, left.Value, right.Value, false);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Is.Empty);
        var environment = subject.Context.Parameters.ToDictionary(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Current),
            binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (subject.Value(binding.Current, inputs[binding.Parameter.Ordinal])));
        Assert.That(new IrInterpreter(subject.Factory).Evaluate(rule.Value, environment).Value!.IntegerNumericValue,
            Is.EqualTo(new BigInteger(expected)));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var replay = subject.Execute(lowering, inputs);
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
    }
}
