using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

public sealed class IrRangeWriteTests
{
    private static readonly ImmutableArray<int> InitialValues = [3, 5, 7, 5];
    [TestCase(1, 2, 8)]
    [TestCase(4, 0, 8)]
    [TestCase(0, 4, 0)]
    [TestCase(3, 1, 3)]
    public void RangeStorePreservesCellsOutsideItsHalfOpenInterval(int start, int length, int expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var variable = factory.CreateVariable("data", type);
        var array = factory.Variable(variable);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var write = builder.ElementStore(entry, factory.CreateOperation(), array, factory.Integer(start), factory.Integer(0), factory.Integer(length));
        builder.Return(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.Add,
            factory.SequenceAccess(array, factory.Integer(0)), factory.SequenceAccess(array, factory.Integer(3))));
        var arguments = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.Add(
            variable, factory.CreateSequenceValue(type, InitialValues.Select(value => factory.CreateIntegerValue(value))));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), arguments);
        Assert.That(write.Length, Is.Not.Null);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(expected));
        Assert.That(result.ConsumedApproximation, Is.False);
    }

    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(5, 0)]
    [TestCase(3, 2)]
    [TestCase(1, int.MaxValue)]
    [TestCase(int.MaxValue, int.MaxValue)]
    public void InvalidRangeNeverReplaysAsANormalWrite(int start, int length)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var variable = factory.CreateVariable("data", type);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.ElementStore(entry, factory.CreateOperation(), factory.Variable(variable), factory.Integer(start), factory.Integer(0), factory.Integer(length));
        builder.Return(entry, factory.CreateOperation(), factory.Integer(0));
        var arguments = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.Add(
            variable, factory.CreateSequenceValue(type, InitialValues.Select(value => factory.CreateIntegerValue(value))));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), arguments);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(result.Unsupported!.Detail, Does.Contain("range store was not in bounds"));
    }

    [Test]
    public void MissingRangeLengthIsUnsupportedWithoutApplyingAWritesWitness()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var length = factory.CreateVariable("length", factory.IntegerType);
        var array = factory.EmptyArray(factory.GetOrCreateSequenceType(factory.IntegerType));
        builder.ElementStore(entry, factory.CreateOperation(), array, factory.Integer(0), factory.Integer(0), factory.Variable(length));
        builder.Return(entry, factory.CreateOperation());
        var writes = 0;
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 1000,
            new IrProgramReplayOptions(_ => null) { WriteObserver = _ => writes++ });
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(writes, Is.Zero);
    }

    [Test]
    public void EmptyRangeOnNullStorageIsUnsupported()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        builder.ElementStore(entry, factory.CreateOperation(), factory.Null(type), factory.Integer(0), factory.Integer(0), factory.Integer(0));
        builder.Return(entry, factory.CreateOperation());
        Assert.That(new IrProgramInterpreter(factory).Execute(builder.Build()).Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
    }

    [Test]
    public void ReferenceRangeStoresClrNullAndPreservesUnclearedIdentity()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.ObjectType);
        var variable = factory.CreateVariable("data", type);
        var array = factory.Variable(variable);
        var first = new object();
        var second = new object();
        object[] actual = [first, second];
        Array.Clear(actual, 1, 1);
        Assert.That(ReferenceEquals(actual[0], first) && actual[1] == null, Is.True);
        string[] covariant = ["first", "second"];
        object[] view = covariant;
        Array.Clear(view, 1, 1);
        Assert.That(covariant[0], Is.EqualTo("first"));
        Assert.That(covariant[1], Is.Null);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var identity = factory.CreateVariable("first", factory.ObjectType);
        builder.ElementStore(entry, factory.CreateOperation(), array, factory.Integer(1), factory.Null(factory.ObjectType), factory.Integer(1));
        builder.Return(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.Equal, factory.SequenceAccess(array, factory.Integer(0)), factory.Variable(identity)),
            factory.Binary(IrBinaryOperator.Equal, factory.SequenceAccess(array, factory.Integer(1)), factory.Null(factory.ObjectType))));
        // The result includes an independently bound reference identity.
        var value = factory.CreateReferenceValue(factory.ObjectType, first);
        var inputBuilder = ImmutableDictionary.CreateBuilder<ScopedIrId<IrVariableTag>, IrValue>();
        inputBuilder[variable] = factory.CreateSequenceValue(type, [value, factory.CreateReferenceValue(factory.ObjectType, second)]);
        inputBuilder[identity] = value;
        var inputs = inputBuilder.ToImmutable();
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), inputs);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Boolean, Is.True);
        Assert.That(result.ConsumedApproximation, Is.False);
    }

    [Test]
    public void RangeLengthAndStartMustBeInt32()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var array = factory.EmptyArray(factory.GetOrCreateSequenceType(factory.IntegerType));
        Assert.Throws<ArgumentException>((Action)(() => builder.ElementStore(entry, factory.CreateOperation(), array, factory.Integer(0), factory.Integer(0), factory.Boolean(false))));
        var wide = factory.GetOrCreateIntegerType(64, true);
        Assert.Throws<ArgumentException>((Action)(() => builder.ElementStore(entry, factory.CreateOperation(), array, factory.Integer(wide, 0), factory.Integer(0), factory.Integer(0))));
    }
}
