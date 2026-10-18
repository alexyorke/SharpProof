using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

public sealed class IrConcreteReplayWitnessTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ObservedValuesAreExactOnlyWhenTheTrustedHostSuppliesThem(bool confirmed)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, value);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var fallback = 0;
        var options = new IrProgramReplayOptions(_ => { fallback++; return factory.CreateBooleanValue(true); })
        { ConcreteHavocValueProvider = _ => confirmed ? factory.CreateBooleanValue(false) : null };
        var replay = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, options);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Boolean, Is.EqualTo(!confirmed));
        Assert.That(replay.ConsumedApproximation, Is.EqualTo(!confirmed));
        Assert.That(fallback, Is.EqualTo(confirmed ? 0 : 1));
    }

    [TestCase(IrHavocOrigin.Input)]
    [TestCase(IrHavocOrigin.SpecResult)]
    public void ConcreteObservationsCannotOverrideOtherHavocOrigins(IrHavocOrigin origin)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, origin, value);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var invoked = false;
        var options = new IrProgramReplayOptions(_ => factory.CreateBooleanValue(true))
        { ConcreteHavocValueProvider = _ => { invoked = true; return factory.CreateBooleanValue(false); } };
        var replay = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, options);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Boolean, Is.True);
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void ConfirmingALaterValueDoesNotEraseAnEarlierApproximationRead()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var unknown = factory.CreateVariable("unknown", factory.BooleanType);
        var observed = factory.CreateVariable("observed", factory.BooleanType);
        var confirmed = factory.CreateVariable("confirmed", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
        builder.Assign(entry, factory.CreateOperation(), observed, factory.Variable(unknown));
        var knownSite = factory.CreateOperation();
        builder.Havoc(entry, knownSite, IrHavocKind.Variables, IrHavocOrigin.Approximation, confirmed);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(confirmed));
        var options = new IrProgramReplayOptions(_ => factory.CreateBooleanValue(true))
        { ConcreteHavocValueProvider = request => request is null ? throw new ArgumentNullException(nameof(request)) : request.Site == knownSite ? factory.CreateBooleanValue(false) : null };
        var replay = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, options);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.True);
        Assert.That(replay.ApproximationVariables, Does.Contain(unknown));
        Assert.That(replay.ApproximationVariables, Does.Not.Contain(confirmed));
    }

    [Test]
    public void AConfirmedValueOfTheWrongTypeStillFailsClosed()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, value);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var options = new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false))
        { ConcreteHavocValueProvider = _ => factory.CreateIntegerValue(0) };
        Assert.That(new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, options).Status,
            Is.EqualTo(IrProgramExecutionStatus.Unsupported));
    }

    [TestCase("System_SByte")]
    [TestCase("System_Byte")]
    [TestCase("System_Int16")]
    [TestCase("System_UInt16")]
    [TestCase("System_Char")]
    [TestCase("System_Int32")]
    [TestCase("System_UInt32")]
    [TestCase("System_Int64")]
    [TestCase("System_UInt64")]
    public void FrameworkReplayDescriptorsAreExactAndCanonical(string primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var site = factory.CreateOperation(IrOpaqueCallSite.DefaultComparerReplay(primitive));
        Assert.That(IrOpaqueCallSite.DefaultComparerReplayPrimitive(factory, site), Is.EqualTo(primitive));
        Assert.That(IrOpaqueCallSite.DefaultComparerReplayPrimitive(factory, factory.CreateOperation()), Is.Null);
    }

    [TestCase("")]
    [TestCase("System_Boolean")]
    [TestCase("System_Decimal")]
    [TestCase("System_IntPtr")]
    [TestCase("System_Int32:extra")]
    [TestCase("system_int32")]
    [TestCase("5")]
    public void UnknownFrameworkReplayDescriptorsAreRejected(string primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOpaqueCallSite.DefaultComparerReplay(primitive)));
        Assert.That(IrOpaqueCallSite.DefaultComparerReplayPrimitive(factory,
            factory.CreateOperation("framework-default-comparer:" + primitive)), Is.Null);
    }
}
