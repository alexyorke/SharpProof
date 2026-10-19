using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class ExceptionResourceLockReplayTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void CertificateCarriesTheObservedLockIdentity(bool boolean)
    {
        var synchronization = new object();
        var (factory, request) = Request(boolean ? "bool" : "object");
        var observed = PassiveCallableVcPlan.ObserveExceptionResourceLock(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            return new(synchronization);
        });
        Assert.That(observed, Is.Not.Null);
        if (boolean)
        { Assert.That(observed!.Boolean, Is.True); }
        else
        { Assert.That(observed!.Reference, Is.SameAs(synchronization)); }
    }

    [Test]
    public void MissingLockDoesNotBecomeAnInventedReference()
    {
        var (factory, boolean) = Request("bool");
        Assert.That(PassiveCallableVcPlan.ObserveExceptionResourceLock(factory, boolean, static _ => new(null))!.Boolean, Is.False);
        var (referenceFactory, reference) = Request("object");
        Assert.That(PassiveCallableVcPlan.ObserveExceptionResourceLock(referenceFactory, reference, static _ => new(null)), Is.Null);
        Assert.That(PassiveCallableVcPlan.ObserveExceptionResourceLock(factory, boolean, static _ => null), Is.Null);
    }

    [TestCase("int", IrHavocOrigin.Approximation, "framework-exception-resource-lock:ArgumentRangeName")]
    [TestCase("object", IrHavocOrigin.Input, "framework-exception-resource-lock:ArgumentRangeName")]
    [TestCase("bool", IrHavocOrigin.SpecResult, "framework-exception-resource-lock:ArgumentRangeName")]
    [TestCase("bool", IrHavocOrigin.Approximation, "framework-exception-resource-lock:UserConstructor")]
    [TestCase("bool", IrHavocOrigin.Approximation, "framework-exception-resource-lock:ArgumentRangeName:spoof")]
    [TestCase("bool", IrHavocOrigin.Approximation, "framework-exception-resource-lock:")]
    [TestCase("bool", IrHavocOrigin.Approximation, "source:ArgumentOutOfRangeException")]
    public void UnownedRequestsDoNotExecuteAnObservation(string type, IrHavocOrigin origin, string description)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(description);
        var (factory, request) = Request(type, origin, description);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveExceptionResourceLock(factory, request, kind =>
        {
            ArgumentNullException.ThrowIfNull(kind);
            invoked = true;
            return new(new object());
        }), Is.Null);
        Assert.That(invoked, Is.False);
    }

    [TestCase("initialization")]
    [TestCase("allocation")]
    [TestCase("resources")]
    public void FailedObservationsCannotPublishAResourceLock(string failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var (factory, request) = Request("bool");
        PassiveCallableVcPlan.ResourceLockObservation? Observe(string kind)
        {
            ArgumentNullException.ThrowIfNull(kind);
            throw failure switch
            {
                "initialization" => new TypeInitializationException("Probe", new InvalidOperationException("probe")),
                "allocation" => Activator.CreateInstance<OutOfMemoryException>(),
                "resources" => new System.Resources.MissingManifestResourceException("probe"),
                _ => new ArgumentOutOfRangeException(nameof(failure))
            };
        }
        Assert.That(PassiveCallableVcPlan.ObserveExceptionResourceLock(factory, request, Observe), Is.Null);
        var (instruction, receiver) = Lock(factory, request.Site);
        Assert.That(PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, instruction, receiver, Observe), Is.False);
    }

    [Test]
    public void CompletionRequiresTheSameObservedObjectAtAnOwnedSite()
    {
        var (factory, request) = Request("bool");
        var (instruction, receiver) = Lock(factory, request.Site);
        Assert.That(PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, instruction, receiver, _ => new(receiver.Reference)), Is.True);
        Assert.That(PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, instruction, receiver, static _ => new(new object())), Is.False);
        Assert.That(PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, instruction, receiver, static _ => null), Is.False);
        Assert.That(PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, instruction, factory.CreateNullValue(factory.ObjectType), static _ => new(new object())), Is.False);
        var (ordinary, ordinaryReceiver) = Lock(factory, factory.CreateOperation("source:lock"));
        Assert.That(PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, ordinary, ordinaryReceiver, static _ => throw new InvalidOperationException("unowned observer")), Is.Null);
    }

    [TestCase(null, false)]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void FrameworkCompletionDoesNotAuthorizeOrdinaryLocksOrEraseApproximation(bool? completion, bool priorApproximation)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("source:lock-projection-control");
        var receiver = factory.CreateVariable("receiver", factory.ObjectType);
        if (priorApproximation)
        {
            var choice = factory.CreateVariable("unknown", factory.BooleanType);
            builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, choice);
            var synchronization = builder.CreateBlock();
            var other = builder.CreateBlock();
            builder.Branch(block, site, factory.Variable(choice), synchronization, other);
            builder.Goto(other, site, synchronization);
            block = synchronization;
        }
        builder.Lock(block, site, factory.Variable(receiver));
        builder.Return(block, site, factory.Integer(17));
        var callbacks = 0;
        bool? prefixApproximation = null;
        var options = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.CreateBooleanValue(false);
        })
        {
            ConcreteLockCompletionProvider = (instruction, value) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                ArgumentNullException.ThrowIfNull(value);
                return completion;
            },
            LockPrefixObserver = (instruction, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                callbacks++;
                prefixApproximation = approximation;
            }
        };
        var replay = new IrProgramInterpreter(factory).Execute(builder.Build(),
            new Dictionary<IrVarId, IrValue> { [receiver] = factory.CreateReferenceValue(factory.ObjectType, new object()) }, 100, options);
        Assert.That(replay.Status, Is.EqualTo(completion == true ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Unsupported));
        Assert.That(callbacks, Is.EqualTo(completion == false ? 0 : 1));
        if (completion != false)
        { Assert.That(prefixApproximation, Is.EqualTo(priorApproximation)); }
        Assert.That(replay.ConsumedApproximation, Is.EqualTo(priorApproximation));
    }

    [Test]
    public void AnUnreviewedModuleNeverExecutesTheMetadataProbe()
    {
        var synchronization = new object();
        var invoked = false;
        object? Observe()
        {
            invoked = true;
            return synchronization;
        }
        Assert.That(PassiveCallableVcPlan.ObserveResourceLockModule(Guid.Empty, Observe), Is.Null);
        Assert.That(invoked, Is.False);
        Assert.That(PassiveCallableVcPlan.ObserveResourceLockModule(new Guid("9f81bf37-7ee9-46d6-8148-5938d2a8d041"), Observe), Is.SameAs(synchronization));
        Assert.That(invoked, Is.True);
    }

    [Test]
    public void ResourceLockBoundariesValidateArguments()
    {
        var (factory, request) = Request("bool");
        var (instruction, receiver) = Lock(factory, request.Site);
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ObserveExceptionResourceLock(null!, request, static _ => null)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ObserveExceptionResourceLock(factory, null!, static _ => null)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ObserveExceptionResourceLock(factory, request, null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.CompleteExceptionResourceLock(null!, instruction, receiver, static _ => null)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, null!, receiver, static _ => null)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, instruction, null!, static _ => null)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.CompleteExceptionResourceLock(factory, instruction, receiver, null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ProbeExceptionResourceLock(null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => PassiveCallableVcPlan.ObserveResourceLockModule(Guid.Empty, null!)));
        Assert.Throws<ArgumentNullException>((Action)(() => IrOpaqueCallSite.ExceptionResourceLock(null!)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOpaqueCallSite.ExceptionResourceLock("UserConstructor")));
        Assert.Throws<ArgumentNullException>((Action)(() => IrOpaqueCallSite.ExceptionResourceLockKind(null!, request.Site)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOpaqueCallSite.ExceptionResourceLockKind(factory, default)));
    }

    private static (IrFactory Factory, IrHavocRequest Request) Request(string type,
        IrHavocOrigin origin = IrHavocOrigin.Approximation, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("observation", type switch
        {
            "bool" => factory.BooleanType,
            "object" => factory.ObjectType,
            _ => factory.IntegerType
        });
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation(description ?? IrOpaqueCallSite.ExceptionResourceLock("ArgumentRangeName"));
        var instruction = builder.Havoc(block, site, IrHavocKind.Variables, origin, variable);
        return (factory, new IrHavocRequest(instruction.Id, site, variable, 0, origin));
    }

    private static (IrLockInstruction Instruction, IrValue Receiver) Lock(IrFactory factory, OperationId site)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (site.IsDefault)
        { throw new ArgumentOutOfRangeException(nameof(site)); }
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var receiver = factory.CreateVariable("synchronization", factory.ObjectType);
        return (builder.Lock(block, site, factory.Variable(receiver)), factory.CreateReferenceValue(factory.ObjectType, new object()));
    }
}
