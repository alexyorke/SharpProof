using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class IntegralConvertInitializationReplayTests
{
    [TestCase("System_Boolean")]
    [TestCase("System_SByte")]
    [TestCase("System_Byte")]
    [TestCase("System_Int16")]
    [TestCase("System_UInt16")]
    [TestCase("System_Char")]
    [TestCase("System_Int32")]
    [TestCase("System_UInt32")]
    [TestCase("System_Int64")]
    [TestCase("System_UInt64")]
    public void IntegralToInt64ReadinessRequiresAnActualSuccessfulFrameworkObservation(string primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var (factory, request) = Request(primitive, IrHavocOrigin.Approximation, wrongType: false);
        var result = PassiveCallableVcPlan.ObserveIntegralConvertInitialization(factory, request, PassiveCallableVcPlan.WarmIntegralConvert);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Boolean, Is.False);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201", Justification = "Inject a failed observation without exhausting process memory.")]
    [TestCase(false)]
    [TestCase(true)]
    public void FailedInitializationNeverCertifiesNormalReplay(bool allocation)
    {
        var (factory, request) = Request("System_Int32", IrHavocOrigin.Approximation, wrongType: false);
        Assert.That(PassiveCallableVcPlan.ObserveIntegralConvertInitialization(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            if (allocation)
            { throw new OutOfMemoryException(); }
            throw new TypeInitializationException("FrameworkProbe", new InvalidOperationException());
        }), Is.Null);
    }

    [TestCase(IrHavocOrigin.Input, false)]
    [TestCase(IrHavocOrigin.SpecResult, false)]
    [TestCase(IrHavocOrigin.Approximation, true)]
    public void UnapprovedHavocsNeverExecuteTheProbe(IrHavocOrigin origin, bool wrongType)
    {
        var (factory, request) = Request("System_Int32", origin, wrongType);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveIntegralConvertInitialization(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            invoked = true;
            return true;
        }), Is.Null);
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void AnObservationThatDisagreesWithTheExpectedStateNeverCertifiesReplay()
    {
        var (factory, request) = Request("System_Int32", IrHavocOrigin.Approximation, wrongType: false);
        Assert.That(PassiveCallableVcPlan.ObserveIntegralConvertInitialization(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            return false;
        }), Is.Null);
    }

    [Test]
    public void IntegralToInt64UnknownAndNonIntegralTypesCannotBeObserved()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOpaqueCallSite.IntegralConvertInitialization("System_String")));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => PassiveCallableVcPlan.WarmIntegralConvert("System_String")));
    }

    [TestCase("framework-integral-convert-ready:System_Double")]
    [TestCase("framework-integral-convert-ready:System_Int32:spoof")]
    [TestCase("framework-integral-convert-ready:")]
    [TestCase("source:System.Convert.ToInt64(System.Int32)")]
    [TestCase("framework-scalar-dictionary-ready:System_Int32")]
    [TestCase("framework-integral-convert-overflow-ancillary")]
    public void MalformedOrUnrelatedSitesNeverRunConvertObservation(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var (factory, request) = Request("System_Int32", IrHavocOrigin.Approximation, wrongType: false, description);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveIntegralConvertInitialization(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            invoked = true;
            return true;
        }), Is.Null);
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void FailedObservationRemainsApproximateInInterpreter()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("ready", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation(IrOpaqueCallSite.IntegralConvertInitialization("System_Int32"));
        builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, variable);
        builder.Return(block, site, factory.Variable(variable));
        var options = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.CreateBooleanValue(false);
        })
        {
            ConcreteHavocValueProvider = request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                return PassiveCallableVcPlan.ObserveIntegralConvertInitialization(factory, request,
                    static primitive =>
                    {
                        ArgumentNullException.ThrowIfNull(primitive);
                        return false;
                    });
            }
        };
        var execution = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 5000, options);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.True);
    }

    private static (IrFactory Factory, IrHavocRequest Request) Request(string primitive, IrHavocOrigin origin, bool wrongType, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", wrongType ? factory.IntegerType : factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation(description ?? IrOpaqueCallSite.IntegralConvertInitialization(primitive));
        var havoc = builder.Havoc(block, site, IrHavocKind.Variables, origin, variable);
        return (factory, new IrHavocRequest(havoc.Id, site, variable, 0, origin));
    }
}
