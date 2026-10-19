using System.Globalization;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class ExceptionInitializationReplayTests
{
    [TestCase("Argument")]
    [TestCase("ArgumentNull")]
    [TestCase("ArgumentNullName")]
    [TestCase("ArgumentRange")]
    [TestCase("ArgumentRangeName")]
    [TestCase("InvalidOperation")]
    [TestCase("InvalidOperationNull")]
    [TestCase("DivideByZero")]
    [TestCase("IOException")]
    [TestCase("ObjectDisposedName")]
    [TestCase("Aggregate")]
    [TestCase("AggregateNull")]
    [TestCase("KeyNotFoundNull")]
    public void NormalReplayRequiresAnActualInvariantFrameworkObservation(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var (factory, request) = Request(kind, IrHavocOrigin.Approximation, false);
            var observed = PassiveCallableVcPlan.ObserveExceptionInitialization(factory, request, PassiveCallableVcPlan.WarmExceptionConstructor);
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
            observed = PassiveCallableVcPlan.WarmExceptionConstructor("ArgumentNull");
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
        var (factory, request) = Request("ArgumentNull", origin, wrongType);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveExceptionInitialization(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            invoked = true;
            return true;
        }), Is.Null);
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void DisagreeingObservationAndUnknownKindsNeverCertifyReplay()
    {
        var (factory, request) = Request("ArgumentNull", IrHavocOrigin.Approximation, false);
        Assert.That(PassiveCallableVcPlan.ObserveExceptionInitialization(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            return false;
        }), Is.Null);
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOpaqueCallSite.ExceptionInitialization("UserConstructor")));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => PassiveCallableVcPlan.WarmExceptionConstructor("UserConstructor")));
    }

    [TestCase("framework-invariant-exception-ready:")]
    [TestCase("framework-invariant-exception-ready:ArgumentNull:spoof")]
    [TestCase("framework-invariant-exception-ready:UserConstructor")]
    [TestCase("source:System.ArgumentNullException")]
    public void UnownedDescriptionsNeverInvokeExceptionObservation(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var (factory, request) = Request("ArgumentNull", IrHavocOrigin.Approximation, false, description);
        Assert.That(PassiveCallableVcPlan.ObserveExceptionInitialization(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            Assert.Fail("Unowned site executed a framework observation.");
            return true;
        }), Is.Null);
    }

    [TestCase("initialization")]
    [TestCase("allocation")]
    [TestCase("resource")]
    public void FailedFrameworkObservationsRemainApproximate(string failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var (factory, request) = Request("ArgumentNull", IrHavocOrigin.Approximation, false);
        Assert.That(PassiveCallableVcPlan.ObserveExceptionInitialization(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            throw failure switch
            {
                "initialization" => new TypeInitializationException("Probe", new InvalidOperationException("probe")),
                "allocation" => Activator.CreateInstance<OutOfMemoryException>(),
                "resource" => new System.Resources.MissingManifestResourceException("probe"),
                _ => new ArgumentOutOfRangeException(nameof(failure))
            };
        }), Is.Null);
    }

    [Test]
    public void OrdinaryNonInvariantCultureDoesNotAuthorizeReplay()
    {
        var previous = CultureInfo.CurrentUICulture;
        bool observed;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            observed = PassiveCallableVcPlan.WarmExceptionConstructor("ArgumentNull");
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        Assert.That(observed, Is.False);
    }

    [Test]
    public void ExceptionObservationArgumentsAreValidated()
    {
        var (factory, request) = Request("ArgumentNull", IrHavocOrigin.Approximation, false);
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ObserveExceptionInitialization(null!, request, _ => true)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ObserveExceptionInitialization(factory, null!, _ => true)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ObserveExceptionInitialization(factory, request, null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.WarmExceptionConstructor(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => IrOpaqueCallSite.IsExceptionInitializationKind(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => IrOpaqueCallSite.ExceptionInitialization(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => IrOpaqueCallSite.ExceptionInitializationKind(null!, request.Site)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOpaqueCallSite.ExceptionInitializationKind(factory, default)));
    }

    private static (IrFactory Factory, IrHavocRequest Request) Request(string kind, IrHavocOrigin origin, bool wrongType, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("ready", wrongType ? factory.IntegerType : factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation(description ?? IrOpaqueCallSite.ExceptionInitialization(kind));
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
