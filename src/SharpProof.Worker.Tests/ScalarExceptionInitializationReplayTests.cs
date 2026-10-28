using System.Globalization;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

internal sealed class ScalarExceptionInitializationReplayTests
{
    [Test]
    public void ExactInvariantScalarFaultCanCompleteConcreteReplay()
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var (factory, request) = Request(IrHavocOrigin.Approximation, false);
            var observed = PassiveCallableVcPlan.ObserveScalarExceptionInitialization(factory, request,
                PassiveCallableVcPlan.ProbeScalarExceptionInitialization);
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Boolean, Is.False);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UserCultureIsRefusedWithoutCallingItsOverrides(bool ui)
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        var culture = new ThrowingCulture();
        bool observed;
        try
        {
            CultureInfo.CurrentUICulture = ui ? culture : CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = ui ? CultureInfo.InvariantCulture : culture;
            culture.Armed = true;
            observed = PassiveCallableVcPlan.ProbeScalarExceptionInitialization("MathAbsInt32");
        }
        finally
        {
            culture.Armed = false;
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
        Assert.That(observed, Is.False);
        Assert.That(culture.Reads, Is.Zero);
    }

    [TestCase(IrHavocOrigin.Input, false)]
    [TestCase(IrHavocOrigin.SpecResult, false)]
    [TestCase(IrHavocOrigin.Approximation, true)]
    public void UnownedHavocsDoNotInvokeTheObservation(IrHavocOrigin origin, bool wrongType)
    {
        var (factory, request) = Request(origin, wrongType);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveScalarExceptionInitialization(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            invoked = true;
            return true;
        }), Is.Null);
        Assert.That(invoked, Is.False);
    }

    [TestCase("framework-invariant-scalar-fault-ready:")]
    [TestCase("framework-invariant-scalar-fault-ready:MathAbsInt32:spoof")]
    [TestCase("framework-invariant-scalar-fault-ready:UserScalar")]
    [TestCase("source:System.Math.Abs")]
    public void UnownedDescriptionsCannotAuthorizeReplay(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(IrOpaqueCallSite.ScalarExceptionInitializationKind(factory, factory.CreateOperation(description)), Is.Null);
    }

    [Test]
    public void DisagreeingObservationCannotAuthorizeReplay()
    {
        var (factory, request) = Request(IrHavocOrigin.Approximation, false);
        Assert.That(PassiveCallableVcPlan.ObserveScalarExceptionInitialization(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            return false;
        }), Is.Null);
    }

    private static (IrFactory Factory, IrHavocRequest Request) Request(IrHavocOrigin origin, bool wrongType)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("ready", wrongType ? factory.IntegerType : factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation(IrOpaqueCallSite.ScalarExceptionInitialization("MathAbsInt32"));
        var instruction = builder.Havoc(block, site, IrHavocKind.Variables, origin, variable);
        return (factory, new IrHavocRequest(instruction.Id, site, variable, 0, origin));
    }

    private sealed class ThrowingCulture() : CultureInfo("en-US")
    {
        public bool Armed { get; set; }
        public int Reads { get; private set; }
        public override string Name
        {
            get
            {
                if (Armed)
                {
                    Reads++;
                    throw new InvalidOperationException("culture override must not run");
                }
                return base.Name;
            }
        }
    }
}
