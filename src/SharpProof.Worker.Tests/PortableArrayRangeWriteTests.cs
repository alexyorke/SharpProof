using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class PortableArrayRangeWriteTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PortableElementAndRangeWritesPreserveArityAndOperands(bool range)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var variable = factory.CreateVariable("data", type);
        var array = factory.Variable(variable);
        builder.Allocate(entry, factory.CreateOperation(), type, variable, factory.Integer(4),
            [factory.Integer(3), factory.Integer(5), factory.Integer(7), factory.Integer(11)]);
        builder.ElementStore(entry, factory.CreateOperation(), array, factory.Integer(1), factory.Integer(0), range ? factory.Integer(2) : null);
        builder.Return(entry, factory.CreateOperation(), factory.SequenceAccess(array, factory.Integer(2)));
        var encoded = PortableIrGraphCodec.Encode(factory, builder.Build(), [], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var row = encoded.Graph.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions).Single(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.Kind == IrInstructionKind.Write);
        Assert.That(row.Items, Has.Length.EqualTo(range ? 2 : 1));
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var write = decoded.Instructions.OfType<IrWriteInstruction>().Single();
        Assert.That(write.Length is not null, Is.EqualTo(range));
        var result = new IrProgramInterpreter(decoded.Factory).Execute(decoded.Program!);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(range ? 0 : 7));
        Assert.That(result.ConsumedApproximation, Is.False);
        var malformedGraph = encoded.Graph.ReplaceRow(row, row with { Items = [] });
        Assert.Throws<InvalidDataException>((Action)(() => PortableIrGraphCodec.Decode(malformedGraph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void PortableRangeRejectsWrongLengthTypeAndExtraOperands(int corruption)
    {
        if (corruption is < 0 or > 4)
        { throw new ArgumentOutOfRangeException(nameof(corruption)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var array = factory.EmptyArray(type);
        builder.ElementStore(entry, factory.CreateOperation(), array, factory.Integer(0), factory.Integer(0), factory.Integer(0));
        builder.Return(entry, factory.CreateOperation(), factory.Integer(0));
        var encoded = PortableIrGraphCodec.Encode(factory, builder.Build(), [factory.Boolean(true), factory.Integer(factory.GetOrCreateIntegerType(64, true), 0)], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var row = encoded.Graph.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions).Single(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.Kind == IrInstructionKind.Write);
        var malformedRow = corruption switch
        {
            3 => row with { A = (int)IrWriteRegion.Field },
            4 => row with { C = encoded.Graph.Roots[1] },
            _ => row with { Items = corruption == 2 ? [row.Items[0], row.Items[1], row.Items[1]] : [row.Items[0], encoded.Graph.Roots[corruption]] }
        };
        var malformedGraph = encoded.Graph.ReplaceRow(row, malformedRow);
        Assert.Throws<InvalidDataException>((Action)(() => PortableIrGraphCodec.Decode(malformedGraph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }
}
