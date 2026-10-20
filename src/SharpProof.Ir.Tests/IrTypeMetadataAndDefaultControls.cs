using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
internal sealed class IrTypeMetadataAndDefaultControls
{
    [TestCase(IrExecutionSemantics.Legacy)]
    [TestCase(IrExecutionSemantics.Total)]
    public void FactoryTypeMetadataAndInterningRemainExact(IrExecutionSemantics semantics)
    {
        var factory = new IrFactory(semantics);
        var identity = factory.CreateIdentity();
        var reference = factory.GetOrCreateReferenceType(identity, "Widget");
        var sequence = factory.GetOrCreateSequenceType(factory.BooleanType);
        var unsigned = factory.GetOrCreateIntegerType(64, signed: false);
        var expectations = new[]
        {
            (Type: factory.BooleanType, Kind: IrTypeKind.Boolean, Name: "bool", Element: (IrTypeId?)null, Width: 0, Signed: false),
            (Type: unsigned, Kind: IrTypeKind.Integer, Name: "ulong", Element: (IrTypeId?)null, Width: 64, Signed: false),
            (Type: factory.StringType, Kind: IrTypeKind.String, Name: "string", Element: (IrTypeId?)null, Width: 0, Signed: false),
            (Type: reference, Kind: IrTypeKind.Reference, Name: "Widget", Element: (IrTypeId?)null, Width: 0, Signed: false),
            (Type: sequence, Kind: IrTypeKind.Sequence, Name: "bool[]", Element: (IrTypeId?)factory.BooleanType, Width: 0, Signed: false)
        };
        using (Assert.EnterMultipleScope())
        {
            foreach (var expected in expectations)
            {
                var info = factory.GetTypeInfo(expected.Type);
                Assert.That(info.Id, Is.EqualTo(expected.Type));
                Assert.That(info.Kind, Is.EqualTo(expected.Kind));
                Assert.That(factory.GetString(info.Name), Is.EqualTo(expected.Name));
                Assert.That(info.ElementType, Is.EqualTo(expected.Element));
                Assert.That(info.Width, Is.EqualTo(expected.Width));
                Assert.That(info.Signed, Is.EqualTo(expected.Signed));
                Assert.That(factory.GetTypeInfo(expected.Type), Is.SameAs(info));
            }
            Assert.That(factory.GetOrCreateReferenceType(identity, "Alias"), Is.EqualTo(reference));
            Assert.That(factory.GetOrCreateSequenceType(factory.BooleanType), Is.EqualTo(sequence));
            Assert.That(factory.GetOrCreateIntegerType(64, signed: false), Is.EqualTo(unsigned));
            var integer = factory.GetTypeInfo(factory.IntegerType);
            Assert.That(integer.Kind, Is.EqualTo(IrTypeKind.Integer));
            Assert.That(integer.Width, Is.EqualTo(semantics == IrExecutionSemantics.Total ? 32 : 0));
            Assert.That(integer.Signed, Is.True);
        }
    }

    [Test]
    public void TotalEvaluationDefaultsPreserveBooleanIntegerAndNullableTypes()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var interpreter = new IrInterpreter(factory);
        var boxed = factory.CreateVariable("boxed", factory.ObjectType);
        var operand = interpreter.Evaluate(factory.Variable(boxed), new Dictionary<IrVarId, IrValue>
        {
            [boxed] = factory.CreateReferenceValue(factory.ObjectType, new object())
        });
        var boolean = interpreter.EvaluateCastValue(factory.BooleanType, operand);
        var integer = interpreter.EvaluateCastValue(factory.IntegerType, operand);
        var strings = factory.GetOrCreateSequenceType(factory.StringType);
        var variable = factory.CreateVariable("empty", strings);
        var nullable = interpreter.Evaluate(factory.SequenceAccess(factory.Variable(variable), factory.Integer(0)),
            new Dictionary<IrVarId, IrValue> { [variable] = factory.CreateSequenceValue(strings, Array.Empty<IrValue>()) });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(boolean.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(boolean.Value!.Type, Is.EqualTo(factory.BooleanType));
            Assert.That(boolean.Value.Boolean, Is.False);
            Assert.That(integer.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(integer.Value!.Type, Is.EqualTo(factory.IntegerType));
            Assert.That(integer.Value.IntegerBits, Is.Zero);
            Assert.That(nullable.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(nullable.Value!.Type, Is.EqualTo(factory.StringType));
            Assert.That(nullable.Value.Kind, Is.EqualTo(IrValueKind.Null));
        }
    }
}
