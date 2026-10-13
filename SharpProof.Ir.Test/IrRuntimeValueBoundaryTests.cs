using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrRuntimeValueBoundaryTests
{
    [TestCase(IrExecutionSemantics.Legacy, false)]
    [TestCase(IrExecutionSemantics.Legacy, true)]
    [TestCase(IrExecutionSemantics.Total, false)]
    [TestCase(IrExecutionSemantics.Total, true)]
    public void EqualityValueOperationChecksActualTypesBeforeKinds(IrExecutionSemantics semantics, bool negate)
    {
        var factory = new IrFactory(semantics);
        var interpreter = new IrInterpreter(factory);
        var left = factory.CreateBooleanValue(true);
        var equal = interpreter.EvaluateEquality(left, factory.CreateBooleanValue(true), negate);
        var different = interpreter.EvaluateEquality(left, factory.CreateBooleanValue(false), negate);
        var incompatible = interpreter.EvaluateEquality(left, factory.CreateIntegerValue(17), negate);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(equal.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(equal.Value!.Boolean, Is.EqualTo(!negate));
            Assert.That(different.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(different.Value!.Boolean, Is.EqualTo(negate));
            Assert.That(incompatible.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(incompatible.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
            Assert.That(incompatible.Unsupported.Detail, Is.EqualTo("Equality requires values with the same type."));
        }
    }

    [TestCase(IrExecutionSemantics.Legacy)]
    [TestCase(IrExecutionSemantics.Total)]
    public void CastValueOperationPreservesIdentityAndRejectsUnsupportedRelation(IrExecutionSemantics semantics)
    {
        var factory = new IrFactory(semantics);
        var interpreter = new IrInterpreter(factory);
        var operand = interpreter.Evaluate(factory.Boolean(true), new Dictionary<IrVarId, IrValue>());
        var identity = interpreter.EvaluateCastValue(factory.BooleanType, operand);
        var incompatible = interpreter.EvaluateCastValue(factory.IntegerType, operand);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity, Is.SameAs(operand));
            Assert.That(identity.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(identity.Value!.Boolean, Is.True);
            Assert.That(incompatible.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(incompatible.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.UnsupportedCast));
            Assert.That(incompatible.Unsupported.Detail, Is.EqualTo("The interpreter has no runtime type relation for this cast."));
        }
    }

    [TestCase(IrExecutionSemantics.Legacy)]
    [TestCase(IrExecutionSemantics.Total)]
    public void CastValueOperationPreservesActualOperandFailure(IrExecutionSemantics semantics)
    {
        var factory = new IrFactory(semantics);
        var variable = factory.CreateVariable("missing", factory.BooleanType);
        var interpreter = new IrInterpreter(factory);
        var operand = interpreter.Evaluate(factory.Variable(variable), new Dictionary<IrVarId, IrValue>());
        var converted = interpreter.EvaluateCastValue(factory.IntegerType, operand);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operand.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(operand.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.MissingVariable));
            Assert.That(converted, Is.SameAs(operand));
        }
    }

    [TestCase(IrExecutionSemantics.Legacy)]
    [TestCase(IrExecutionSemantics.Total)]
    public void CastValueOperationPreservesBoxedIntegerRules(IrExecutionSemantics semantics)
    {
        var factory = new IrFactory(semantics);
        var variable = factory.CreateVariable("boxed", factory.ObjectType);
        var interpreter = new IrInterpreter(factory);
        object validBox = semantics == IrExecutionSemantics.Total ? (object)17 : 17L;
        object invalidBox = semantics == IrExecutionSemantics.Total ? (object)17L : 17;
        IrEvaluationResult EvaluateBox(object box)
        {
            return interpreter.Evaluate(
                factory.Variable(variable),
                new Dictionary<IrVarId, IrValue> { [variable] = factory.CreateReferenceValue(factory.ObjectType, box) });
        }
        var valid = interpreter.EvaluateCastValue(factory.IntegerType, EvaluateBox(validBox));
        var invalid = interpreter.EvaluateCastValue(factory.IntegerType, EvaluateBox(invalidBox));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(valid.Value!.Type, Is.EqualTo(factory.IntegerType));
            Assert.That(valid.Value.Integer, Is.EqualTo(17L));
            if (semantics == IrExecutionSemantics.Total)
            {
                Assert.That(invalid.Status, Is.EqualTo(IrEvaluationStatus.Value));
                Assert.That(invalid.Value!.Type, Is.EqualTo(factory.IntegerType));
                Assert.That(invalid.Value.Integer, Is.Zero);
            }
            else
            {
                Assert.That(invalid.Status, Is.EqualTo(IrEvaluationStatus.Exception));
                Assert.That(invalid.Exception!.Kind, Is.EqualTo(IrExceptionKind.InvalidCast));
                Assert.That(invalid.Exception.Detail, Is.EqualTo("The concrete reference does not contain a boxed integer."));
            }
        }
    }
}
