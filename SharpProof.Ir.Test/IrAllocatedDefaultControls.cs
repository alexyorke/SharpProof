using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrAllocatedDefaultControls
{
    [TestCase(IrTypeKind.Boolean)]
    [TestCase(IrTypeKind.Integer)]
    [TestCase(IrTypeKind.String)]
    [TestCase(IrTypeKind.Reference)]
    [TestCase(IrTypeKind.Sequence)]
    public void AllocationDefaultsPreserveTheElementType(IrTypeKind kind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var element = kind switch
        {
            IrTypeKind.Boolean => factory.BooleanType,
            IrTypeKind.Integer => factory.IntegerType,
            IrTypeKind.String => factory.StringType,
            IrTypeKind.Reference => factory.ObjectType,
            IrTypeKind.Sequence => factory.GetOrCreateSequenceType(factory.IntegerType),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var sequence = factory.GetOrCreateSequenceType(element);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var target = factory.CreateVariable("array", sequence);
        builder.Allocate(entry, factory.CreateOperation(), sequence, target, factory.Integer(1));
        builder.Return(entry, factory.CreateOperation(), factory.SequenceAccess(factory.Variable(target), factory.Integer(0)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Type, Is.EqualTo(element));
        if (kind == IrTypeKind.Boolean)
        {
            Assert.That(result.ReturnValue.Boolean, Is.False);
        }
        else if (kind == IrTypeKind.Integer)
        {
            Assert.That(result.ReturnValue.IntegerBits, Is.Zero);
        }
        else
        {
            Assert.That(result.ReturnValue.Kind, Is.EqualTo(IrValueKind.Null));
        }
    }

    [TestCase(0, true)]
    [TestCase(8, false)]
    [TestCase(8, true)]
    [TestCase(16, false)]
    [TestCase(16, true)]
    [TestCase(32, false)]
    [TestCase(32, true)]
    [TestCase(64, false)]
    [TestCase(64, true)]
    public void IntegerZeroBitsAndNumericCreationAgree(int width, bool signed)
    {
        var factory = new IrFactory(width == 0 ? IrExecutionSemantics.Legacy : IrExecutionSemantics.Total);
        var type = width == 0 ? factory.IntegerType : factory.GetOrCreateIntegerType(width, signed);
        var numeric = factory.CreateIntegerValue(type, 0L);
        var bits = factory.CreateIntegerValueFromBits(type, 0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bits.Type, Is.EqualTo(numeric.Type));
            Assert.That(bits.IntegerBits, Is.EqualTo(numeric.IntegerBits));
            Assert.That(bits.IntegerWidth, Is.EqualTo(numeric.IntegerWidth));
            Assert.That(bits.IntegerSigned, Is.EqualTo(numeric.IntegerSigned));
            Assert.That(bits.IntegerNumericValue, Is.EqualTo(numeric.IntegerNumericValue));
        }
    }
}
