using System.Numerics;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompoundShiftStorageAlgebraCoverageTests
{
    [TestCase(32, false)]
    [TestCase(64, true)]
    [TestCase(16, true)]
    [TestCase(0, false)]
    public void OwnedStorageRuleRetainsRejectedCountDomain(int countWidth, bool isSigned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var countType = countWidth == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(countWidth, isSigned);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var count = factory.CreateVariable("count", countType);
        var rule = CSharpOperationSemantics.ApplyCompoundShiftScalar(factory, BinaryOperatorKind.LeftShift,
            factory.Variable(value), factory.Variable(count), false);
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value).Value!.Boolean, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OwnedNarrowStorageRequiresKnownCheckedContext(bool invalidCount)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var valueType = factory.GetOrCreateIntegerType(8, true);
        var countType = invalidCount ? factory.BooleanType : factory.IntegerType;
        var value = factory.CreateVariable("value", valueType);
        var count = factory.CreateVariable("count", countType);
        var rule = CSharpOperationSemantics.ApplyCompoundShiftScalar(factory, BinaryOperatorKind.LeftShift,
            factory.Variable(value), factory.Variable(count), null);
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedMutation));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value).Value!.Boolean, Is.False);
    }

    [TestCase(false, 64, -128, false)]
    [TestCase(true, 64, -128, true)]
    [TestCase(true, 17, 34, false)]
    public void OwnedNarrowStoragePreservesValueAndOverflowFault(bool isChecked, int input, int expected, bool overflow)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, true);
        var value = factory.CreateVariable("value", type);
        var count = factory.CreateVariable("count", factory.IntegerType);
        var rule = CSharpOperationSemantics.ApplyCompoundShiftScalar(factory, BinaryOperatorKind.LeftShift,
            factory.Variable(value), factory.Variable(count), isChecked);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Value.Type, Is.EqualTo(type));
        var environment = new Dictionary<IrVarId, IrValue>
        {
            [value] = factory.CreateIntegerValue(type, input),
            [count] = factory.CreateIntegerValue(factory.IntegerType, 1)
        };
        var interpreter = new IrInterpreter(factory);
        Assert.That(interpreter.Evaluate(rule.Value, environment).Value!.IntegerNumericValue,
            Is.EqualTo(new BigInteger(expected)));
        if (isChecked)
        {
            Assert.That(rule.Throws, Has.Length.EqualTo(1));
            Assert.That(rule.Throws[0].Kind, Is.EqualTo(IrExceptionKind.Overflow));
            Assert.That(interpreter.Evaluate(rule.Throws[0].Condition, environment).Value!.Boolean,
                Is.EqualTo(overflow));
        }
        else
        {
            Assert.That(rule.Throws, Is.Empty);
        }
    }

    [TestCase("sbyte Target(sbyte value, int count) { unchecked { value <<= count; } return value; }", (sbyte)64, false, -128)]
    [TestCase("sbyte Target(sbyte value, int count) { checked { value <<= count; } return value; }", (sbyte)64, true, -128)]
    [TestCase("sbyte Target(sbyte value, int count) { checked { value <<= count; } return value; }", (sbyte)17, false, 34)]
    public void ActualCheckedStorageMatchesNativeValueOrOverflow(string source, sbyte input, bool overflow, int expected)
    {
        using var subject = TypedProgramSubject.Create(source);
        object[] inputs = [input, 1];
        var native = subject.Invoke(inputs);
        if (overflow)
        {
            Assert.That(native, Is.TypeOf<OverflowException>());
        }
        else
        {
            Assert.That(native, Is.EqualTo((sbyte)expected));
        }
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, inputs);
        Assert.That(replay.ConsumedApproximation, Is.False);
        if (overflow)
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(expected)));
        }
    }
}
