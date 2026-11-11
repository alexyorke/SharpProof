using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
internal sealed class IrRuntimeValueBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void EqualityValueOperationChecksActualTypesBeforeKinds(bool negate)
    {
        var factory = new IrFactory();
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

    [Test]
    public void CastValueOperationPreservesIdentityAndRejectsUnsupportedRelation()
    {
        var factory = new IrFactory();
        var interpreter = new IrInterpreter(factory);
        var operand = interpreter.Evaluate(factory.Boolean(true), global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty);
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

    [Test]
    public void CastValueOperationPreservesActualOperandFailure()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("missing", factory.BooleanType);
        var interpreter = new IrInterpreter(factory);
        var operand = interpreter.Evaluate(factory.Variable(variable), global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty);
        var converted = interpreter.EvaluateCastValue(factory.IntegerType, operand);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operand.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(operand.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.MissingVariable));
            Assert.That(converted, Is.SameAs(operand));
        }
    }

    [Test]
    public void CastValueOperationPreservesBoxedIntegerRules()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("boxed", factory.ObjectType);
        var interpreter = new IrInterpreter(factory);
        object validBox = 17;
        object invalidBox = 17L;
        IrEvaluationResult EvaluateBox(object box)
        {
            System.ArgumentNullException.ThrowIfNull(box);
            return interpreter.Evaluate(
                factory.Variable(variable),
                global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateReferenceValue(factory.ObjectType, box)));
        }
        var valid = interpreter.EvaluateCastValue(factory.IntegerType, EvaluateBox(validBox));
        var invalid = interpreter.EvaluateCastValue(factory.IntegerType, EvaluateBox(invalidBox));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(valid.Value!.Type, Is.EqualTo(factory.IntegerType));
            Assert.That(valid.Value.Integer, Is.EqualTo(17L));
            Assert.That(invalid.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(invalid.Value!.Type, Is.EqualTo(factory.IntegerType));
            Assert.That(invalid.Value.Integer, Is.Zero);
        }
    }
}
