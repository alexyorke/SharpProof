using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrClosedTermDispatchTests
{
    [Test]
    public void PrinterPreservesGrammarForEveryRegisteredTermShape()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.Variable(factory.CreateVariable("flag", factory.BooleanType));
        var integer = factory.Variable(factory.CreateVariable("integer", factory.IntegerType));
        var ownerType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Owner");
        var owner = factory.Variable(factory.CreateVariable("owner", ownerType));
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence = factory.Variable(factory.CreateVariable("sequence", sequenceType));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), ownerType, "Read", factory.IntegerType, false, factory.IntegerType);
        var shapes = new (IrTerm Term, string Text)[]
        {
            (factory.Boolean(true), "true"),
            (factory.Integer(2), "2"),
            (factory.String("x"), "\"x\""),
            (factory.Null(ownerType), "((" + TypeName(factory, ownerType) + ")null)"),
            (factory.EmptyArray(sequenceType), "empty(" + TypeName(factory, sequenceType) + ")"),
            (integer, "v1"),
            (factory.PureOpaque(member, owner, integer), "pure:" + member + "(v2; v1)"),
            (factory.Unary(IrUnaryOperator.Not, flag), "(!v0)"),
            (factory.Binary(IrBinaryOperator.Add, integer, factory.Integer(1)), "(v1 + 1)"),
            (factory.Conditional(flag, integer, factory.Integer(2)), "(v0 ? v1 : 2)"),
            (factory.Cast(factory.ObjectType, owner), "((" + TypeName(factory, factory.ObjectType) + ")v2)"),
            (factory.Length(sequence), "len(v3)"),
            (factory.SequenceAccess(sequence, integer), "v3[v1]")
        };
        Assert.That(shapes.Select(shape => shape.Term.Kind), Is.EquivalentTo(Enum.GetValues<IrTermKind>()));
        var printer = new IrPrinter(factory);
        foreach (var shape in shapes)
        {
            Assert.That(printer.Print(shape.Term), Is.EqualTo(shape.Text), shape.Term.Kind.ToString());
        }
        var foreign = new IrFactory();
        Assert.That(Assert.Throws<ArgumentException>((Action)(() => printer.Print(foreign.Integer(1))))!.ParamName, Is.EqualTo("term"));
        Assert.That(Assert.Throws<ArgumentNullException>((Action)(() => printer.Print(null!)))!.ParamName, Is.EqualTo("term"));
    }

    [TestCase(IrBinaryOperator.ShiftLeft, 6L)]
    [TestCase(IrBinaryOperator.ShiftRight, 1L)]
    public void LegacyUnspecifiedWidthShiftsRemainUnsupportedWhileTypedShiftsEvaluate(IrBinaryOperator operation, long expected)
    {
        var legacy = new IrFactory();
        var left = legacy.CreateVariable("left", legacy.IntegerType);
        var right = legacy.CreateVariable("right", legacy.IntegerType);
        var legacyTerm = legacy.Binary(operation, legacy.Variable(left), legacy.Variable(right));
        var unsupported = new IrInterpreter(legacy).Evaluate(legacyTerm, new Dictionary<IrVarId, IrValue>
        { [left] = legacy.CreateIntegerValue(3), [right] = legacy.CreateIntegerValue(1) });
        Assert.That(unsupported.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(unsupported.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.UnsupportedOperation));
        Assert.That(unsupported.Unsupported.Detail, Is.EqualTo("Unsupported integer operator: " + operation + "."));

        var total = new IrFactory(IrExecutionSemantics.Total);
        var typedLeft = total.CreateVariable("left", total.IntegerType);
        var typedRight = total.CreateVariable("right", total.IntegerType);
        var typedTerm = total.Binary(operation, total.Variable(typedLeft), total.Variable(typedRight));
        var supported = new IrInterpreter(total).Evaluate(typedTerm, new Dictionary<IrVarId, IrValue>
        { [typedLeft] = total.CreateIntegerValue(3), [typedRight] = total.CreateIntegerValue(1) });
        Assert.That(supported.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(supported.Value!.Integer, Is.EqualTo(expected));
        Assert.That(supported.Value.Type, Is.EqualTo(total.IntegerType));
    }

    private static string TypeName(IrFactory factory, IrTypeId type)
    {
        return "\"" + factory.GetString(factory.GetTypeInfo(type).Name) + "\"#t" + type.Value;
    }
}
