namespace SharpProof.Smt.Test;

internal sealed class DefaultComparerValueModelTests
{
    [Test]
    public async Task KernelRejectsAnObservedCacheClaimedAsAForeignNominalView()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var declared = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "declared");
        var foreign = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "foreign");
        var variable = factory.CreateVariable("value", foreign);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), declared,
            "framework-default-comparer-value:System_Int32", declared, true, []);
        var identity = new object();
        var comparison = factory.Binary(IrBinaryOperator.Equal, factory.Cast(factory.ObjectType, factory.Variable(variable)),
            factory.Cast(factory.ObjectType, factory.PureOpaque(member, null)));
        var query = new VerificationQuery(factory, [new Assumption(factory, comparison, new LoweredJustification(factory.CreateOperation()))],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [variable]);
        var result = BackendCheckResult.Satisfiable(new BackendModel([KeyValuePair.Create(variable, factory.CreateReferenceValue(foreign, identity))]));
        var kernel = new ProofKernel(new FixedBackend(result))
        {
            ConcreteOpaqueValueProvider = term => term is null ? throw new ArgumentNullException(nameof(term)) : factory.CreateReferenceValue(term.Type, identity)
        };
        var outcome = await kernel.VerifyAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    [Test]
    public async Task ConflictingObservedIdentitiesCannotValidateAnAliasedModel()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "first");
        var second = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "second");
        var firstMember = factory.GetOrCreateMember(factory.CreateIdentity(), first,
            "framework-default-comparer-value:" + "System_Int32", first, true, []);
        var secondMember = factory.GetOrCreateMember(factory.CreateIdentity(), second,
            "framework-default-comparer-value:" + "System_Int64", second, true, []);
        var left = factory.PureOpaque(firstMember, null);
        var right = factory.PureOpaque(secondMember, null);
        var firstInstance = new object();
        var secondInstance = new object();
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions())
        {
            ConcreteOpaqueValueProvider = term => term is null ? throw new ArgumentNullException(nameof(term)) :
                factory.CreateReferenceValue(term.Type, term.Member == firstMember ? firstInstance : secondInstance)
        };
        var assumption = new Assumption(factory, factory.Binary(IrBinaryOperator.Equal,
            factory.Cast(factory.ObjectType, left), factory.Cast(factory.ObjectType, right)), new LoweredJustification(factory.CreateOperation()));
        var query = new VerificationQuery(factory, [assumption],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), []);
        Assert.That((await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken)).Status, Is.EqualTo(BackendCheckStatus.Unknown));
    }

    [TestCase("observed")]
    [TestCase("unavailable")]
    [TestCase("wrong-type")]
    [TestCase("null")]
    [TestCase("kernel-unavailable")]
    [TestCase("kernel-disagrees")]
    public async Task CachedValuesRequireConsistentConcreteObservations(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", factory.ObjectType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "framework-default-comparer-value:" + "System_Int32", factory.ObjectType, true, []);
        var term = factory.PureOpaque(member, null);
        var instance = new object();
        IrValue? Observe(IrOpaqueTerm observed)
        {
            ArgumentNullException.ThrowIfNull(observed);
            return scenario switch
            {
                "unavailable" => null,
                "wrong-type" => factory.CreateIntegerValue(0),
                "null" => factory.CreateNullValue(factory.ObjectType),
                _ => factory.CreateReferenceValue(factory.ObjectType, instance)
            };
        }
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions()) { ConcreteOpaqueValueProvider = Observe };
        var kernel = new ProofKernel(session)
        {
            ConcreteOpaqueValueProvider = scenario == "kernel-unavailable" ? null : scenario == "kernel-disagrees"
                ? _ => factory.CreateReferenceValue(factory.ObjectType, new object()) : Observe
        };
        var assumption = new Assumption(factory, factory.Binary(IrBinaryOperator.Equal, factory.Variable(variable), term),
            new LoweredJustification(factory.CreateOperation()));
        var query = new VerificationQuery(factory, [assumption],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [variable]);
        var outcome = await kernel.VerifyAsync(query, TestContext.CurrentContext.CancellationToken);
        if (scenario == "observed")
        {
            Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome is UnknownOutcome unknown ? unknown.Reason.ToString() : outcome.GetType().Name);
            Assert.That(((RefutedOutcome)outcome).Model.Assignments[variable].Reference, Is.SameAs(instance));
        }
        else
        {
            Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
            if (scenario is "unavailable" or "kernel-unavailable")
            { Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(AbstentionReason.CounterexampleNotReplayable)); }
        }
    }

    private sealed class FixedBackend(BackendCheckResult result) : ISmtBackend
    {
        private readonly BackendCheckResult _result = result ?? throw new ArgumentNullException(nameof(result));
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }
}
