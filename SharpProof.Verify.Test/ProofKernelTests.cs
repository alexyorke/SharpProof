using System.Reflection;

namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class ProofKernelTests {
    [Test]
    public async Task UnsatCreatesAProvenOutcomeWithOnlyRequestedEvidence() {
        var fixture = CreateFixture();
        var secondOperation = fixture.Factory.CreateOperation("second");
        var query = new VerificationQuery(
            fixture.Factory,
            [
                new Assumption(
                    fixture.Factory,
                    fixture.Factory.Boolean(true),
                    new LoweredJustification(fixture.Operation)),
                new Assumption(
                    fixture.Factory,
                    fixture.Factory.Boolean(true),
                    new LoweredJustification(secondOperation))
            ],
            fixture.Goal);
        var outcome = await new ProofKernel(
            new StubBackend(BackendCheckResult.Unsatisfiable([1])))
            .VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(((ProvenOutcome)outcome).Core.Length, Is.EqualTo(1));
        Assert.That(
            ((LoweredJustification)((ProvenOutcome)outcome).Core[0]).Operation,
            Is.EqualTo(secondOperation));
        Assert.That(OutcomeCachePolicy.IsCacheable(outcome), Is.True);
    }

    [Test]
    public async Task SatBecomesRefutedOnlyAfterConcreteReplay() {
        var fixture = CreateFixture();
        var model = new BackendModel([
            KeyValuePair.Create(fixture.Variable, fixture.Factory.CreateIntegerValue(0))
        ]);
        var outcome = await new ProofKernel(
            new StubBackend(BackendCheckResult.Satisfiable(model)))
            .VerifyAsync(fixture.Query);

        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(
            ((RefutedOutcome)outcome).Model.Assignments[fixture.Variable].Integer,
            Is.Zero);
        Assert.That(OutcomeCachePolicy.IsCacheable(outcome), Is.True);
    }

    [Test]
    public async Task SpuriousOrIncompleteModelsAbstain() {
        var fixture = CreateFixture();
        var trueModel = new BackendModel([
            KeyValuePair.Create(fixture.Variable, fixture.Factory.CreateIntegerValue(2))
        ]);
        var spurious = await new ProofKernel(
            new StubBackend(BackendCheckResult.Satisfiable(trueModel)))
            .VerifyAsync(fixture.Query);
        var incomplete = await new ProofKernel(
            new StubBackend(BackendCheckResult.Satisfiable(new BackendModel([]))))
            .VerifyAsync(fixture.Query);

        Assert.That(
            ((UnknownOutcome)spurious).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
        Assert.That(
            ((UnknownOutcome)incomplete).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
        Assert.That(OutcomeCachePolicy.IsCacheable(spurious), Is.False);
        Assert.That(OutcomeCachePolicy.IsCacheable(incomplete), Is.False);
    }

    [Test]
    public async Task OpaqueEvidenceCannotValidateARefutation() {
        var fixture = CreateFixture();
        var member = fixture.Factory.GetOrCreateMember(
            fixture.Factory.CreateIdentity(),
            fixture.Factory.ObjectType,
            "UnknownPredicate",
            fixture.Factory.BooleanType,
            isStatic: true);
        var query = new VerificationQuery(
            fixture.Factory,
            [
                new Assumption(
                    fixture.Factory,
                    fixture.Factory.PureOpaque(member, receiver: null),
                    new LoweredJustification(fixture.Operation))
            ],
            fixture.Goal);
        var model = new BackendModel([
            KeyValuePair.Create(fixture.Variable, fixture.Factory.CreateIntegerValue(0))
        ]);
        var outcome = await new ProofKernel(
            new StubBackend(BackendCheckResult.Satisfiable(model)))
            .VerifyAsync(query);

        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    [Test]
    public async Task BackendFailuresStayTypedAndUncacheable() {
        var fixture = CreateFixture();
        foreach (var pair in new[] {
                     (BackendFailureReason.UnsupportedEncoding, AbstentionReason.UnsupportedEncoding),
                     (BackendFailureReason.ResourceLimit, AbstentionReason.ResourceLimit),
                     (BackendFailureReason.Timeout, AbstentionReason.Timeout),
                     (BackendFailureReason.Unavailable, AbstentionReason.BackendUnavailable),
                     (BackendFailureReason.InfrastructureFailure, AbstentionReason.InfrastructureFailure)
                 }) {
            var outcome = await new ProofKernel(
                new StubBackend(BackendCheckResult.Unknown(pair.Item1)))
                .VerifyAsync(fixture.Query);
            Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(pair.Item2));
            Assert.That(OutcomeCachePolicy.IsCacheable(outcome), Is.False);
        }
    }

    [Test]
    public async Task MalformedUnsatCoreCannotCreateAProof() {
        var fixture = CreateFixture();
        var outcome = await new ProofKernel(
            new StubBackend(BackendCheckResult.Unsatisfiable([4])))
            .VerifyAsync(fixture.Query);

        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.MalformedBackendResult));
    }

    [Test]
    public void ApproximationIsNotAProofJustification() {
        Assert.That(
            typeof(ProofJustification).IsAssignableFrom(typeof(ApproximatedJustification)),
            Is.False);
        Assert.That(
            typeof(Assumption)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Single()
                .GetParameters()[2]
                .ParameterType,
            Is.EqualTo(typeof(ProofJustification)));
    }

    [Test]
    public void CancellationPropagatesInsteadOfBecomingSemanticUnknown() {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> action = () => new ProofKernel(
                new StubBackend(BackendCheckResult.Unsatisfiable([])))
            .VerifyAsync(fixture.Query, cancellation.Token);

        Assert.ThrowsAsync<OperationCanceledException>(action);
    }

    private static Fixture CreateFixture() {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var operation = factory.CreateOperation("comparison");
        var predicate = factory.Binary(
            IrBinaryOperator.GreaterThan,
            factory.Variable(variable),
            factory.Integer(0));
        var assumption = new Assumption(
            factory,
            factory.Boolean(true),
            new LoweredJustification(operation));
        var goal = new Goal(
            factory,
            predicate,
            ProofDiagnosticKind.Precondition,
            new SourceLocationId(0));
        return new Fixture(
            factory,
            variable,
            operation,
            goal,
            new VerificationQuery(factory, [assumption], goal));
    }

    private sealed class StubBackend(BackendCheckResult result) : ISmtBackend {
        private readonly BackendCheckResult _result = result;

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class Fixture(
        IrFactory factory,
        IrVarId variable,
        OperationId operation,
        Goal goal,
        VerificationQuery query) {
        internal IrFactory Factory { get; } = factory;
        internal IrVarId Variable { get; } = variable;
        internal OperationId Operation { get; } = operation;
        internal Goal Goal { get; } = goal;
        internal VerificationQuery Query { get; } = query;
    }
}
