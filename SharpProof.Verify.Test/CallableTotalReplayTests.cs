using System.Collections.Immutable;
using System.Numerics;

namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class CallableTotalReplayTests
{
    [TestCase("postcondition", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("guard", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("read-then-overwritten", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("spec-result", AbstentionReason.CounterexampleReplayFailed)]
    [TestCase("unbound-input", AbstentionReason.CounterexampleReplayFailed)]
    public async Task UnvalidatedNondeterminismCannotCreateARefutation(string scenario, AbstentionReason reason)
    {
        var outcome = await Replay(scenario);
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(reason));
    }

    [TestCase("overwritten")]
    [TestCase("untaken")]
    [TestCase("prestate")]
    [TestCase("input")]
    public async Task OnlyValuesActuallyReadByBodyOrContractNeedReplayEvidence(string scenario)
    {
        var outcome = await Replay(scenario);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var value = ((RefutedOutcome)outcome).Model.Assignments.Single(pair =>
            pair.Value.Kind == IrValueKind.Integer).Value;
        Assert.That(value.Integer, Is.EqualTo(-1));
    }

    [TestCase("missing", AbstentionReason.CounterexampleNotReplayable)]
    [TestCase("false", AbstentionReason.PostconditionMayBeUndefined)]
    public async Task TotalPostconditionRequiresAnExplicitSuccessfulGuard(string scenario, AbstentionReason reason)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new CallableReplayContext(null, true, ImmutableDictionary<IrVarId, IrVarId>.Empty,
            ImmutableDictionary<IrVarId, IrVarId?>.Empty, [], factory.Boolean(false),
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, 100, [],
            postconditionGuard: scenario == "missing" ? null : factory.Boolean(false), replayOptions: null);
        var outcome = await new ProofKernel(new StubBackend(new BackendModel([])))
            .VerifyCallableAsync(Query(factory, []), context);
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
            .VerifyCallableAsync(Query(factory, [variable]), context);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(((RefutedOutcome)outcome).Model.Assignments[variable].IntegerNumericValue,
            Is.EqualTo(new BigInteger(ulong.MaxValue)));
    }

    private static async Task<ProofOutcome> Replay(string scenario)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = factory.CreateVariable("canonical", factory.IntegerType);
        var bodyParameter = factory.CreateVariable("body", factory.IntegerType);
        var temporary = factory.CreateVariable("temporary", factory.IntegerType);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var origin = scenario is "input" or "unbound-input" ? IrHavocOrigin.Input
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
            replayOptions: new IrProgramReplayOptions(_ => factory.CreateIntegerValue(scenario == "input" ? 2 : -1)));
        var model = new BackendModel([
            KeyValuePair.Create(parameter, factory.CreateIntegerValue(-1)),
            KeyValuePair.Create(flag, factory.CreateBooleanValue(false))
        ]);
        return await new ProofKernel(new StubBackend(model))
            .VerifyCallableAsync(Query(factory, [parameter, flag]), context);
    }

    private static VerificationQuery Query(IrFactory factory, IrVarId[] variables)
    {
        return new VerificationQuery(factory, [], new Goal(factory, factory.Boolean(false),
            ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [.. variables]);
    }

    private sealed class StubBackend(BackendModel model) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackendCheckResult.Satisfiable(model));
        }
    }
}
