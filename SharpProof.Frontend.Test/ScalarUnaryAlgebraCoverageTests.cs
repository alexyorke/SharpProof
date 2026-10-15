using System.Numerics;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ScalarUnaryAlgebraCoverageTests
{
    [TestCase(UnaryOperatorKind.Plus, 8, 32, 17L, 17L, false, false)]
    [TestCase(UnaryOperatorKind.Plus, 32, 32, 17L, 17L, false, false)]
    [TestCase(UnaryOperatorKind.Minus, 32, 32, 17L, -17L, false, false)]
    [TestCase(UnaryOperatorKind.Minus, 32, 32, int.MinValue, int.MinValue, false, false)]
    [TestCase(UnaryOperatorKind.Minus, 32, 32, int.MinValue, int.MinValue, true, true)]
    [TestCase(UnaryOperatorKind.Minus, 64, 64, long.MinValue, long.MinValue, true, true)]
    [TestCase(UnaryOperatorKind.Minus, 64, 64, 17L, -17L, true, false)]
    [TestCase(UnaryOperatorKind.BitwiseNegation, 32, 32, 17L, -18L, false, false)]
    public void OwnedIntegerAlgebraPreservesValuesPromotionAndOverflow(
        UnaryOperatorKind kind, int inputWidth, int targetWidth, long input,
        long expected, bool isChecked, bool overflow)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var inputType = factory.GetOrCreateIntegerType(inputWidth, true);
        var target = factory.GetOrCreateIntegerType(targetWidth, true);
        var variable = factory.CreateVariable("value", inputType);
        var rule = CSharpOperationSemantics.ApplyUnaryScalar(
            factory, kind, isChecked, factory.Variable(variable), target);
        var environment = new Dictionary<IrVarId, IrValue>
        {
            [variable] = factory.CreateIntegerValue(inputType, input)
        };
        var interpreter = new IrInterpreter(factory);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Value.Type, Is.EqualTo(target));
        Assert.That(interpreter.Evaluate(rule.Value, environment).Value!.IntegerNumericValue,
            Is.EqualTo(new BigInteger(expected)));
        if (isChecked && kind == UnaryOperatorKind.Minus)
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

    [TestCase(true)]
    [TestCase(false)]
    public void OwnedBooleanNotAlgebraPreservesBooleanResult(bool input)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var rule = CSharpOperationSemantics.ApplyUnaryScalar(factory,
            UnaryOperatorKind.Not, false, factory.Boolean(input), factory.BooleanType);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value).Value!.Boolean, Is.EqualTo(!input));
    }

    [TestCase(UnaryOperatorKind.Not, false)]
    [TestCase(UnaryOperatorKind.None, false)]
    [TestCase(UnaryOperatorKind.Plus, true)]
    public void OwnedRejectedUnaryDomainsReturnTheTypedScalarAbstention(
        UnaryOperatorKind kind, bool booleanOperand)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(32, true);
        IrTerm operand = booleanOperand ? factory.Boolean(true) : factory.Integer(type, 17L);
        var rule = CSharpOperationSemantics.ApplyUnaryScalar(factory, kind, false, operand,
            booleanOperand ? factory.BooleanType : type);
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value).Value!.Boolean, Is.False);
    }

    [TestCase("bool Target(bool value) => !value;", true)]
    [TestCase("int Target(sbyte value) => +value;", (sbyte)17)]
    [TestCase("int Target(int value) => ~value;", 17)]
    [TestCase("int Target(int value) => -value;", int.MinValue)]
    [TestCase("int Target(int value) => checked(-value);", int.MinValue)]
    [TestCase("long Target(long value) => checked(-value);", 17L)]
    public void AuthenticCompiledUnaryProgramsAgreeWithExactLoweredExecution(string source, object input)
    {
        using var subject = TypedProgramSubject.Create(source);
        var native = subject.Invoke([input]);
        var lowered = subject.Lower();
        Assert.That(lowered.Classification.IsExact, Is.True);
        var execution = subject.Execute(lowered, [input]);
        Assert.That(execution.ConsumedApproximation, Is.False);
        if (native is OverflowException)
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(native, Is.Not.InstanceOf<Exception>());
            if (native is bool boolean)
            {
                Assert.That(execution.ReturnValue!.Boolean, Is.EqualTo(boolean));
            }
            else
            {
                Assert.That(execution.ReturnValue!.IntegerNumericValue,
                    Is.EqualTo(new BigInteger(Convert.ToInt64(native, System.Globalization.CultureInfo.InvariantCulture))));
            }
        }
    }
}
