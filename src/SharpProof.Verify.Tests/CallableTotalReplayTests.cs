using System.Collections.Immutable;
using System.Numerics;

namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class CallableTotalReplayTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task OriginalBodyReadsValidatedOldHeapRoles(bool array)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
        var entry = factory.CreateVariable("a", type);
        var current = factory.CreateVariable("b", type);
        var old = factory.CreateVariable("c", type);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Assign(block, factory.CreateOperation(), current, factory.Variable(entry));
        builder.Assign(block, factory.CreateOperation(), old, factory.Variable(entry));
        if (array)
        { builder.ElementStore(block, factory.CreateOperation(), factory.Variable(current), factory.Integer(0), factory.Integer(7)); }
        else
        { builder.FieldStore(block, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(current), field, factory.Integer(7)); }
        builder.Return(block, factory.CreateOperation(), array
            ? factory.SequenceAccess(factory.Variable(old), factory.Integer(0))
            : factory.PureOpaque(field, factory.Variable(old)));
        var context = new CallableReplayContext(builder.Build(), false,
            ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(entry, entry).Add(current, entry),
            ImmutableDictionary<IrVarId, IrVarId?>.Empty.Add(old, entry), [result],
            factory.Binary(IrBinaryOperator.Equal, factory.Variable(result), factory.Integer(7)),
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, 100, [],
            postconditionGuard: factory.Boolean(true), replayOptions: null);
        var value = array ? factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)])
            : factory.CreateReferenceValue(type, new IrObjectState().WithField(field, factory.CreateIntegerValue(3)));
        var outcome = await new ProofKernel(new StubBackend(new BackendModel([KeyValuePair.Create(entry, value)])))
            .VerifyCallableAsync(Query(factory, [entry]), context, TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
    }

    [TestCase("postcondition", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("guard", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("read-then-overwritten", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("spec-result", AbstentionReason.CounterexampleReplayFailed)]
    [TestCase("unbound-input", AbstentionReason.CounterexampleReplayFailed)]
    public async Task UnvalidatedNondeterminismCannotCreateARefutation(string scenario, AbstentionReason reason)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        var outcome = await Replay(scenario);
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(reason));
    }

    [Test]
    public async Task MissingApproximationProviderBindingFailsClosed()
    {
        var outcome = await Replay("provider-missing-binding");
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    [TestCase("overwritten")]
    [TestCase("untaken")]
    [TestCase("prestate")]
    [TestCase("input")]
    [TestCase("input-no-provider")]
    public async Task OnlyValuesActuallyReadByBodyOrContractNeedReplayEvidence(string scenario)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        var outcome = await Replay(scenario);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var value = ((RefutedOutcome)outcome).Model.Assignments.Single(pair =>
            pair.Value.Kind == IrValueKind.Integer).Value;
        Assert.That(value.Integer, Is.EqualTo(-1));
    }

    [TestCase("missing", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("false", AbstentionReason.PostconditionMayBeUndefined)]
    [TestCase("unresolved", AbstentionReason.CounterexampleReplayFailed)]
    public async Task TotalPostconditionRequiresAnExplicitSuccessfulGuard(string scenario, AbstentionReason reason)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new CallableReplayContext(null, true, ImmutableDictionary<IrVarId, IrVarId>.Empty,
            ImmutableDictionary<IrVarId, IrVarId?>.Empty, [], factory.Boolean(false),
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, 100, [],
            postconditionGuard: scenario == "missing" ? null
                : scenario == "unresolved" ? factory.Variable(factory.CreateVariable("unresolved", factory.BooleanType))
                : factory.Boolean(false), replayOptions: null);
        var outcome = await new ProofKernel(new StubBackend(new BackendModel([])))
            .VerifyCallableAsync(Query(factory, []), context, TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(reason));
    }

    [Test]
    public async Task Unsigned64ModelAndDomainReplayKeepTheHighBit()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(64, false);
        var variable = factory.CreateVariable("value", type);
        var value = factory.CreateIntegerValue(type, ulong.MaxValue);
        var context = new CallableReplayContext(null, true, ImmutableDictionary<IrVarId, IrVarId>.Empty,
            ImmutableDictionary<IrVarId, IrVarId?>.Empty, [], factory.Boolean(false),
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty.Add(variable, (0, ulong.MaxValue)), 100, [],
            postconditionGuard: factory.Boolean(true), replayOptions: null);
        var outcome = await new ProofKernel(new StubBackend(new BackendModel([KeyValuePair.Create(variable, value)])))
            .VerifyCallableAsync(Query(factory, [variable]), context, TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(((RefutedOutcome)outcome).Model.Assignments[variable].IntegerNumericValue,
            Is.EqualTo(new BigInteger(ulong.MaxValue)));
    }

    [TestCase(1, false, true)]
    [TestCase(-1, false, false)]
    [TestCase(1, true, false)]
    [TestCase(11, false, false)]
    public async Task TrivialCallableReplayRequiresValidatedEntryEvidence(
        int input, bool missingPreState, bool refuted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var entry = factory.CreateVariable("entry", factory.IntegerType);
        var old = factory.CreateVariable("old", factory.IntegerType);
        var constraint = factory.Binary(IrBinaryOperator.GreaterThanOrEqual,
            factory.Variable(entry), factory.Integer(0));
        var preState = ImmutableDictionary<IrVarId, IrVarId?>.Empty
            .Add(old, missingPreState ? null : entry);
        var domain = ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty
            .Add(entry, (0, 10));
        var context = new CallableReplayContext(null, true,
            ImmutableDictionary<IrVarId, IrVarId>.Empty, preState, [],
            factory.Boolean(false), domain, 100, [],
            postconditionGuard: factory.Boolean(true), replayOptions: null);
        context = new CallableReplayContext(context, [constraint]);
        var model = new BackendModel([
            KeyValuePair.Create(entry, factory.CreateIntegerValue(input))
        ]);

        var outcome = await new ProofKernel(new StubBackend(model))
            .VerifyCallableAsync(Query(factory, [entry]), context, TestContext.CurrentContext.CancellationToken);

        if (refuted)
        {
            Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(((RefutedOutcome)outcome).Model.Assignments[entry].Integer,
                Is.EqualTo(input));
        }
        else
        {
            Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(((UnknownOutcome)outcome).Reason,
                Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
        }
    }

    private static async Task<ProofOutcome> Replay(string scenario)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = factory.CreateVariable("canonical", factory.IntegerType);
        var bodyParameter = factory.CreateVariable("body", factory.IntegerType);
        var temporary = factory.CreateVariable("temporary", factory.IntegerType);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var origin = scenario is "input" or "input-no-provider" or "unbound-input" ? IrHavocOrigin.Input
            : scenario == "spec-result" ? IrHavocOrigin.SpecResult : IrHavocOrigin.Approximation;
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, origin, bodyParameter);
        if (scenario == "read-then-overwritten")
        {
            builder.Assign(entry, factory.CreateOperation(), temporary, factory.Variable(bodyParameter));
            builder.Assign(entry, factory.CreateOperation(), temporary, factory.Integer(-2));
        }
        if (scenario == "overwritten")
        {
            builder.Assign(entry, factory.CreateOperation(), bodyParameter, factory.Integer(-2));
        }
        builder.Return(entry, factory.CreateOperation());
        var comparison = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter), factory.Integer(0));
        var context = new CallableReplayContext(builder.Build(), false,
            scenario == "unbound-input" ? ImmutableDictionary<IrVarId, IrVarId>.Empty
                : ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(bodyParameter, parameter),
            scenario == "prestate" ? ImmutableDictionary<IrVarId, IrVarId?>.Empty.Add(parameter, parameter)
                : ImmutableDictionary<IrVarId, IrVarId?>.Empty,
            [], scenario == "guard" ? factory.Boolean(false)
                : scenario == "untaken" ? factory.Binary(IrBinaryOperator.AndAlso, factory.Variable(flag), comparison)
                : comparison,
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, 100, [],
            postconditionGuard: scenario == "guard" ? comparison : factory.Boolean(true),
            // Input substitutions must be ignored; only the bound entry value is authoritative.
            replayOptions: scenario == "input-no-provider" ? null
                : new IrProgramReplayOptions(_ => scenario == "provider-missing-binding"
                    ? throw new KeyNotFoundException("Modeled approximation has no provider binding.")
                    : factory.CreateIntegerValue(scenario == "input" ? 2 : -1)));
        var model = new BackendModel([
            KeyValuePair.Create(parameter, factory.CreateIntegerValue(-1)),
            KeyValuePair.Create(flag, factory.CreateBooleanValue(false))
        ]);
        return await new ProofKernel(new StubBackend(model))
            .VerifyCallableAsync(Query(factory, [parameter, flag]), context, TestContext.CurrentContext.CancellationToken);
    }

    private static VerificationQuery Query(IrFactory factory, IrVarId[] variables)
    {
        System.ArgumentNullException.ThrowIfNull(variables, nameof(variables));
        System.ArgumentNullException.ThrowIfNull(factory);
        return new VerificationQuery(factory, [], new Goal(factory, factory.Boolean(false),
            ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [.. variables]);
    }

    private sealed class StubBackend(BackendModel model) : ISmtBackend
    {
        private readonly BackendModel _guarded_model = model ?? throw new System.ArgumentNullException(nameof(model));
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            System.ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackendCheckResult.Satisfiable(_guarded_model));
        }
    }
}
