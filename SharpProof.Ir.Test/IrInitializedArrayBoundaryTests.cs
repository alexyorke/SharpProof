using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrInitializedArrayBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void InitializersRequireTheirExactConstantLength(bool variableLength)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var target = factory.CreateVariable("array", sequenceType);
        var length = variableLength
            ? factory.Variable(factory.CreateVariable("length", factory.IntegerType))
            : (IrTerm)factory.Integer(2);
        var exception = Assert.Throws<ArgumentException>((Action)(() =>
            builder.Allocate(entry, factory.CreateOperation(), sequenceType, target,
                length, [factory.Integer(42)])));
        Assert.That(exception!.ParamName, Is.EqualTo("initialValues"));
        Assert.That(exception.Message, Does.StartWith("An initialized array requires its exact constant length."));

        builder.Allocate(entry, factory.CreateOperation(), sequenceType, target,
            factory.Integer(1), [factory.Integer(42)]);
        builder.Return(entry, factory.CreateOperation(),
            factory.SequenceAccess(factory.Variable(target), factory.Integer(0)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Type, Is.EqualTo(factory.IntegerType));
        Assert.That(result.ReturnValue.Integer, Is.EqualTo(42));
    }
}
