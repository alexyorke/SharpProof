using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class FrameworkValueReplayTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201", Justification = "Inject a failed framework observation without exhausting the test process memory.")]
    [TestCase("null")]
    [TestCase("initialization")]
    [TestCase("allocation")]
    public void AnUnavailableFrameworkObservationDoesNotConfirmTheHavoc(string failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("throws", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var site = factory.CreateOperation(IrOpaqueCallSite.DefaultComparerReplay("System_Int32"));
        var havoc = builder.Havoc(entry, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, variable);
        var request = new IrHavocRequest(havoc.Id, site, variable, 0, IrHavocOrigin.Approximation);
        object? Read(string primitive)
        {
            ArgumentNullException.ThrowIfNull(primitive);
            return failure switch
            {
                "null" => null,
                "initialization" => throw new TypeInitializationException("FrameworkProbe", new InvalidOperationException()),
                "allocation" => throw new OutOfMemoryException(),
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            };
        }
        Assert.That(PassiveCallableVcPlan.ObserveFrameworkValue(factory, request, Read), Is.Null);
    }

    [TestCase("input")]
    [TestCase("spec-result")]
    [TestCase("unknown-site")]
    [TestCase("wrong-type")]
    public void UnapprovedReplayRequestsNeverInvokeTheFrameworkGetter(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", scenario == "wrong-type" ? factory.IntegerType : factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var site = scenario == "unknown-site" ? factory.CreateOperation("opaque-call:511:Unrelated") :
            factory.CreateOperation(IrOpaqueCallSite.DefaultComparerReplay("System_Int32"));
        var origin = scenario == "input" ? IrHavocOrigin.Input : scenario == "spec-result" ? IrHavocOrigin.SpecResult : IrHavocOrigin.Approximation;
        var havoc = builder.Havoc(entry, site, IrHavocKind.Variables, origin, variable);
        var request = new IrHavocRequest(havoc.Id, site, variable, 0, origin);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveFrameworkValue(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            invoked = true;
            return new object();
        }), Is.Null);
        Assert.That(invoked, Is.False);
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
    public void FrameworkReferenceObservationsUseTheActualSingleton(string primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", factory.ObjectType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var site = factory.CreateOperation(IrOpaqueCallSite.DefaultComparerReplay(primitive));
        var havoc = builder.Havoc(entry, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, variable);
        var request = new IrHavocRequest(havoc.Id, site, variable, 0, IrHavocOrigin.Approximation);
        var observed = PassiveCallableVcPlan.ObserveFrameworkValue(factory, request, PassiveCallableVcPlan.ReadDefaultComparer);
        Assert.That(observed, Is.Not.Null);
        Assert.That(observed!.Reference, Is.SameAs(PassiveCallableVcPlan.ReadDefaultComparer(primitive)));
    }

    [Test]
    public void UnknownFrameworkPrimitivesCannotBeExecuted()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => PassiveCallableVcPlan.ReadDefaultComparer("System_Boolean")));
    }
}
