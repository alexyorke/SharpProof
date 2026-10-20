using NUnit.Framework;

namespace SharpProof.Ir.Test;

// Matches the established IrKernelTests negative immutable-value boundary.
// Malformed values only enter rejection tests; they never create owned IR
// terms, programs, solver witnesses, or proof evidence.
[TestFixture]
internal sealed class IrMalformedRuntimeValueTests
{
    [TestCase(IrUnaryOperator.Not, "Boolean negation requires a boolean value.")]
    [TestCase(IrUnaryOperator.Negate, "Integer negation requires an integer value.")]
    public void UnaryOperatorsRejectMalformedRuntimeKinds(IrUnaryOperator operation, string diagnostic)
    {
        System.ArgumentNullException.ThrowIfNull(diagnostic);
        var factory = new IrFactory();
        var boolean = operation == IrUnaryOperator.Not;
        var type = boolean ? factory.BooleanType : factory.IntegerType;
        var variable = factory.CreateVariable("operand", type);
        var term = factory.Unary(operation, factory.Variable(variable));
        var validValue = boolean ? factory.CreateBooleanValue(true) : factory.CreateIntegerValue(3);
        var valid = new IrInterpreter(factory).Evaluate(term, new Dictionary<IrVarId, IrValue> { [variable] = validValue });
        Assert.That(valid.Status, Is.EqualTo(IrEvaluationStatus.Value));
        if (boolean)
        {
            Assert.That(valid.Value!.Boolean, Is.False);
        }
        else
        {
            Assert.That(valid.Value!.Integer, Is.EqualTo(-3));
        }
        var payload = boolean ? factory.CreateIntegerValue(3) : factory.CreateBooleanValue(true);
        AssertRejected(factory, term, new Dictionary<IrVarId, IrValue> { [variable] = Malformed(type, payload) }, diagnostic);
    }

    [TestCase(IrBinaryOperator.AndAlso, true, "Conditional conjunction requires boolean values.")]
    [TestCase(IrBinaryOperator.OrElse, true, "Conditional disjunction requires boolean values.")]
    [TestCase(IrBinaryOperator.AndAlso, false, "Boolean operators require boolean values.")]
    [TestCase(IrBinaryOperator.OrElse, false, "Boolean operators require boolean values.")]
    public void BooleanOperatorsRejectMalformedSelectedOperands(IrBinaryOperator operation, bool leftMalformed, string diagnostic)
    {
        System.ArgumentNullException.ThrowIfNull(diagnostic);
        var factory = new IrFactory();
        var left = factory.CreateVariable("left", factory.BooleanType);
        var right = factory.CreateVariable("right", factory.BooleanType);
        var term = factory.Binary(operation, factory.Variable(left), factory.Variable(right));
        var values = new Dictionary<IrVarId, IrValue>
        {
            [left] = factory.CreateBooleanValue(operation == IrBinaryOperator.AndAlso),
            [right] = factory.CreateBooleanValue(true)
        };
        Assert.That(new IrInterpreter(factory).Evaluate(term, values).Value!.Boolean, Is.True);
        values[leftMalformed ? left : right] = Malformed(factory.BooleanType, factory.CreateIntegerValue(1));
        AssertRejected(factory, term, values, diagnostic);
    }

    [TestCase(IrBinaryOperator.StringConcat, "String concatenation requires string values.")]
    [TestCase(IrBinaryOperator.StringEquals, "String equality requires string values.")]
    public void StringOperatorsRejectMalformedRuntimeKinds(IrBinaryOperator operation, string diagnostic)
    {
        System.ArgumentNullException.ThrowIfNull(diagnostic);
        var factory = new IrFactory();
        var left = factory.CreateVariable("left", factory.StringType);
        var right = factory.CreateVariable("right", factory.StringType);
        var term = factory.Binary(operation, factory.Variable(left), factory.Variable(right));
        var values = new Dictionary<IrVarId, IrValue>
        { [left] = factory.CreateStringValue("same"), [right] = factory.CreateStringValue("same") };
        var valid = new IrInterpreter(factory).Evaluate(term, values);
        Assert.That(valid.Status, Is.EqualTo(IrEvaluationStatus.Value));
        if (operation == IrBinaryOperator.StringEquals)
        { Assert.That(valid.Value!.Boolean, Is.True); }
        else
        { Assert.That(valid.Value!.String, Is.EqualTo("samesame")); }
        values[left] = Malformed(factory.StringType, factory.CreateBooleanValue(true));
        AssertRejected(factory, term, values, diagnostic);
    }

    [Test]
    public void EqualityRejectsIncompatibleRuntimeKindsWithMatchingDeclaredTypes()
    {
        var factory = new IrFactory();
        var left = factory.CreateVariable("left", factory.StringType);
        var right = factory.CreateVariable("right", factory.StringType);
        var term = factory.Binary(IrBinaryOperator.Equal, factory.Variable(left), factory.Variable(right));
        var values = new Dictionary<IrVarId, IrValue>
        { [left] = factory.CreateStringValue("same"), [right] = factory.CreateStringValue("same") };
        Assert.That(new IrInterpreter(factory).Evaluate(term, values).Value!.Boolean, Is.True);
        values[left] = Malformed(factory.StringType, factory.CreateBooleanValue(true));
        AssertRejected(factory, term, values, "Equality requires values with compatible runtime kinds.");
    }

    [Test]
    public void ConditionalRejectsMalformedGuardInsteadOfSelectingAnArm()
    {
        var factory = new IrFactory();
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var term = factory.Conditional(factory.Variable(flag), factory.Integer(1), factory.Integer(2));
        var values = new Dictionary<IrVarId, IrValue> { [flag] = factory.CreateBooleanValue(true) };
        Assert.That(new IrInterpreter(factory).Evaluate(term, values).Value!.Integer, Is.EqualTo(1));
        values[flag] = Malformed(factory.BooleanType, factory.CreateIntegerValue(1));
        AssertRejected(factory, term, values, "A conditional guard requires a boolean value.");
    }

    [Test]
    public void LengthRejectsMalformedStringRuntimeKind()
    {
        var factory = new IrFactory();
        var text = factory.CreateVariable("text", factory.StringType);
        var term = factory.Length(factory.Variable(text));
        var values = new Dictionary<IrVarId, IrValue> { [text] = factory.CreateStringValue("abc") };
        Assert.That(new IrInterpreter(factory).Evaluate(term, values).Value!.Integer, Is.EqualTo(3));
        values[text] = Malformed(factory.StringType, factory.CreateBooleanValue(true));
        AssertRejected(factory, term, values, "Length requires a string or sequence value.");
    }

    [TestCase(true, "Sequence access requires a sequence value.")]
    [TestCase(false, "Sequence access requires an integer index.")]
    public void SequenceAccessRejectsMalformedRuntimeKinds(bool sequenceMalformed, string diagnostic)
    {
        System.ArgumentNullException.ThrowIfNull(diagnostic);
        var factory = new IrFactory();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence = factory.CreateVariable("sequence", type);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var term = factory.SequenceAccess(factory.Variable(sequence), factory.Variable(index));
        var values = new Dictionary<IrVarId, IrValue>
        {
            [sequence] = factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)]),
            [index] = factory.CreateIntegerValue(0)
        };
        Assert.That(new IrInterpreter(factory).Evaluate(term, values).Value!.Integer, Is.EqualTo(3));
        values[sequenceMalformed ? sequence : index] = Malformed(sequenceMalformed ? type : factory.IntegerType, factory.CreateBooleanValue(true));
        AssertRejected(factory, term, values, diagnostic);
    }

    private static IrValue Malformed(IrTypeId declaredType, IrValue payload)
    {
        if (declaredType.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(declaredType));
        }
        System.ArgumentNullException.ThrowIfNull(payload);
        return new IrValue(declaredType, payload.Kind, payload.Payload);
    }

    private static void AssertRejected(IrFactory factory, IrTerm term, Dictionary<IrVarId, IrValue> values, string diagnostic)
    {
        System.ArgumentNullException.ThrowIfNull(diagnostic);
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(term);
        System.ArgumentNullException.ThrowIfNull(values);
        var result = new IrInterpreter(factory).Evaluate(term, values);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(result.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
        Assert.That(result.Unsupported.Detail, Is.EqualTo(diagnostic));
    }
}
