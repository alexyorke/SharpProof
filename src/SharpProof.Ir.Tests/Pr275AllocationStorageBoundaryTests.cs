using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275AllocationStorageBoundaryTests
{
    [Test]
    public void AnArrayLengthRequiresStorageBeforeAnInstructionCanBeAppended()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequence = factory.GetOrCreateSequenceType(factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            builder.Allocate(block, factory.CreateOperation(), sequence, length: factory.Integer(1))));
        Assert.That(error!.ParamName, Is.EqualTo("length"));
    }

    [Test]
    public void ArrayStorageMustHaveTheActualAllocatedSequenceType()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequence = factory.GetOrCreateSequenceType(factory.IntegerType);
        var scalar = factory.CreateVariable("scalar", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            builder.Allocate(block, factory.CreateOperation(), sequence, scalar, factory.Integer(1))));
        Assert.That(error!.ParamName, Is.EqualTo("target"));
    }

    [Test]
    public void ValidOwnedArrayStorageReplaysItsInitializedContents()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequence = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", sequence);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var allocation = builder.Allocate(block, factory.CreateOperation(), sequence, array, factory.Integer(1));
        Assert.That(allocation.Target, Is.EqualTo(array));
        builder.Return(block, factory.CreateOperation(),
            factory.SequenceAccess(factory.Variable(array), factory.Integer(0)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.Zero);
    }
}
