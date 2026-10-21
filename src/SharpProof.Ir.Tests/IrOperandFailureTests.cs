using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrOperandFailureTests
{
    [Test]
    public void MissingReceiverAndSequenceValuesPropagateToTheirReads()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Receiver");
        var receiver = factory.Variable(factory.CreateVariable("receiver", type));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), type, "Read", factory.IntegerType, false);
        var sequence = factory.Variable(factory.CreateVariable("sequence", factory.GetOrCreateSequenceType(factory.IntegerType)));
        var interpreter = new IrInterpreter(factory);
        foreach (var root in ImmutableArray.Create<IrTerm>(factory.PureOpaque(member, receiver), factory.SequenceAccess(sequence, factory.Integer(0)), factory.Cast(factory.ObjectType, receiver)))
        {
            var missing = interpreter.Evaluate(root);
            Assert.That(missing.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(missing.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.MissingVariable));
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ElementStoresStopAtTheFirstMissingOperand(int missingOperand)
    {
        if (missingOperand < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(missingOperand)); }

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", sequenceType);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var store = builder.ElementStore(entry, factory.CreateOperation(), factory.Variable(array), factory.Variable(index), factory.Variable(value));
        builder.Return(entry, factory.CreateOperation());
        var inputs = global::System.Collections.Immutable.ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        inputs[array] = factory.CreateSequenceValue(sequenceType, [factory.CreateIntegerValue(0)]);
        inputs[index] = factory.CreateIntegerValue(0);
        inputs[value] = factory.CreateIntegerValue(5);
        var program = builder.Build();
        var interpreter = new IrProgramInterpreter(factory);
        Assert.That(interpreter.Execute(program, inputs.ToImmutable()).Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        _ = inputs.Remove(System.Collections.Immutable.ImmutableArray.Create(array, index, value)[missingOperand]);
        var writes = 0;
        var result = interpreter.Execute(program, inputs.ToImmutable(), maximumSteps: 10000, replayOptions: new IrProgramReplayOptions(_ => null)
        { WriteObserver = _ => writes++ });
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(writes, Is.Zero);
    }

    [Test]
    public void BuilderRejectsInvalidStoreAndLoadTypesBeforeExecution()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var array = factory.Variable(factory.CreateVariable("array", factory.GetOrCreateSequenceType(factory.IntegerType)));
        Assert.Throws<ArgumentException>((Action)(() => builder.ElementStore(entry, factory.CreateOperation(), array, factory.Boolean(true), factory.Integer(1))));
        Assert.Throws<ArgumentException>((Action)(() => builder.SequenceLocation(factory.Integer(1), factory.Integer(0))));
        var location = builder.SequenceLocation(array, factory.Integer(0));
        Assert.Throws<ArgumentException>((Action)(() => builder.Load(entry, factory.CreateOperation(), factory.CreateVariable("boolean", factory.BooleanType), location)));
        builder.Return(entry, factory.CreateOperation());
        Assert.That(new IrProgramInterpreter(factory).Execute(builder.Build()).Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
    }
}
