using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Testing.Test;

[TestFixture]
public sealed class IrUnboxingDifferentialRegressionTests
{
    [Test]
    public void InterpreterUsesCSharpObjectUnboxingSemantics()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.ObjectType);
        var variable = factory.Variable(value);
        var unboxInteger = factory.Cast(factory.IntegerType, variable);
        var unboxBoolean = factory.Cast(factory.BooleanType, variable);
        var interpreter = new IrInterpreter(factory);

        var nullInteger = Evaluate(
            interpreter,
            unboxInteger,
            value,
            factory.CreateNullValue(factory.ObjectType));
        var nullBoolean = Evaluate(
            interpreter,
            unboxBoolean,
            value,
            factory.CreateNullValue(factory.ObjectType));
        var wrongIntegerBox = Evaluate(
            interpreter,
            unboxInteger,
            value,
            factory.CreateReferenceValue(factory.ObjectType, true));
        var wrongBooleanBox = Evaluate(
            interpreter,
            unboxBoolean,
            value,
            factory.CreateReferenceValue(factory.ObjectType, 17L));
        var integer = Evaluate(
            interpreter,
            unboxInteger,
            value,
            factory.CreateReferenceValue(factory.ObjectType, 17L));
        var boolean = Evaluate(
            interpreter,
            unboxBoolean,
            value,
            factory.CreateReferenceValue(factory.ObjectType, true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                nullInteger.Exception?.Kind,
                Is.EqualTo(IrExceptionKind.NullReference));
            Assert.That(
                nullBoolean.Exception?.Kind,
                Is.EqualTo(IrExceptionKind.NullReference));
            Assert.That(
                wrongIntegerBox.Exception?.Kind,
                Is.EqualTo(IrExceptionKind.InvalidCast));
            Assert.That(
                wrongBooleanBox.Exception?.Kind,
                Is.EqualTo(IrExceptionKind.InvalidCast));
            Assert.That(integer.Value!.Integer, Is.EqualTo(17));
            Assert.That(boolean.Value!.Boolean, Is.True);
        }
    }

    [Test]
    public void DifferentialOracleAgreesWithCompiledCSharpObjectUnboxing()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.ObjectType);
        var variable = factory.Variable(value);
        var unboxInteger = factory.Cast(factory.IntegerType, variable);
        var unboxBoolean = factory.Cast(factory.BooleanType, variable);
        var oracle = new IrCSharpDifferentialOracle(factory);
        var cases = new (string Name, IrTerm Term, IrValue Value)[]
        {
            (
                "null integer",
                unboxInteger,
                factory.CreateNullValue(factory.ObjectType)),
            (
                "null boolean",
                unboxBoolean,
                factory.CreateNullValue(factory.ObjectType)),
            (
                "wrong integer box",
                unboxInteger,
                factory.CreateReferenceValue(factory.ObjectType, true)),
            (
                "wrong boolean box",
                unboxBoolean,
                factory.CreateReferenceValue(factory.ObjectType, 17L)),
            (
                "integer",
                unboxInteger,
                factory.CreateReferenceValue(factory.ObjectType, 17L)),
            (
                "boolean",
                unboxBoolean,
                factory.CreateReferenceValue(factory.ObjectType, true))
        };

        foreach (var @case in cases)
        {
            var result = oracle.Compare(
                @case.Term,
                new Dictionary<IrVarId, IrValue>
                {
                    [value] = @case.Value
                });

            Assert.That(
                result.Status,
                Is.EqualTo(DifferentialStatus.Agreement),
                @case.Name + ": " + result.Detail);
        }
    }

    private static IrEvaluationResult Evaluate(
        IrInterpreter interpreter,
        IrTerm term,
        IrVarId variable,
        IrValue value)
    {
        return interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [variable] = value
            });
    }
}
