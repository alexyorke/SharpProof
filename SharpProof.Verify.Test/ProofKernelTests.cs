using System.Reflection;

namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class ProofKernelTests
{
    [Test]
    public async Task UnsatCreatesAProvenOutcomeWithOnlyRequestedEvidence()
    {
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
    }

    [Test]
    public async Task SatBecomesRefutedOnlyAfterConcreteReplay()
    {
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
    }

    [Test]
    public async Task FormulaAndRequestedModelVariablesFormAnExactDeterministicSet()
    {
        var factory = new IrFactory();
        var integer = factory.CreateVariable("integer", factory.IntegerType);
        var boolean = factory.CreateVariable("boolean", factory.BooleanType);
        var formula = factory.CreateVariable("formula", factory.BooleanType);
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Variable(formula),
                ProofDiagnosticKind.Postcondition,
                new SourceLocationId(0)),
            [boolean, integer]);
        var model = new BackendModel([
            KeyValuePair.Create(boolean, factory.CreateBooleanValue(true)),
            KeyValuePair.Create(integer, factory.CreateIntegerValue(42)),
            KeyValuePair.Create(formula, factory.CreateBooleanValue(false))
        ]);

        var outcome = await new ProofKernel(
            new StubBackend(BackendCheckResult.Satisfiable(model)))
            .VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(query.ModelVariables, Has.Length.EqualTo(3));
        Assert.That(query.ModelVariables[0], Is.EqualTo(integer));
        Assert.That(query.ModelVariables[1], Is.EqualTo(boolean));
        Assert.That(query.ModelVariables[2], Is.EqualTo(formula));
        Assert.That(((RefutedOutcome)outcome).Model.Assignments, Has.Count.EqualTo(3));

        var invented = factory.CreateVariable("invented", factory.IntegerType);
        var inventedOutcome = await new ProofKernel(new StubBackend(
                BackendCheckResult.Satisfiable(new BackendModel(
                    model.Assignments.Append(KeyValuePair.Create(
                        invented, factory.CreateIntegerValue(7)))))))
            .VerifyAsync(query);
        Assert.That(inventedOutcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(
            ((UnknownOutcome)inventedOutcome).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    [Test]
    public async Task MissingOrMalformedRequestedModelBindingsAbstain()
    {
        var factory = new IrFactory();
        var integer = factory.CreateVariable("integer", factory.IntegerType);
        var text = factory.CreateVariable("text", factory.StringType);
        var goal = new Goal(
            factory,
            factory.Boolean(false),
            ProofDiagnosticKind.Postcondition,
            new SourceLocationId(0));
        var exactQuery = new VerificationQuery(factory, [], goal, [integer]);
        var missing = await new ProofKernel(new StubBackend(
                BackendCheckResult.Satisfiable(new BackendModel([]))))
            .VerifyAsync(exactQuery);
        var wrongType = await new ProofKernel(new StubBackend(
                BackendCheckResult.Satisfiable(new BackendModel([
                    KeyValuePair.Create(integer, factory.CreateBooleanValue(false))
                ]))))
            .VerifyAsync(exactQuery);
        var unsupported = await new ProofKernel(new StubBackend(
                BackendCheckResult.Satisfiable(new BackendModel([
                    KeyValuePair.Create(text, factory.CreateStringValue("value"))
                ]))))
            .VerifyAsync(new VerificationQuery(factory, [], goal, [text]));

        ProofOutcome[] outcomes = [missing, wrongType, unsupported];
        Assert.That(outcomes, Has.All.TypeOf<UnknownOutcome>());
        Assert.That(
            outcomes.Cast<UnknownOutcome>()
                .Select(static outcome => outcome.Reason),
            Has.All.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    [Test]
    public void RequestedModelVariablesMustBeUniqueAndFactoryOwned()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var foreignFactory = new IrFactory();
        var foreign = foreignFactory.CreateVariable("foreign", foreignFactory.IntegerType);
        var goal = new Goal(
            factory,
            factory.Boolean(false),
            ProofDiagnosticKind.Postcondition,
            new SourceLocationId(0));

        Assert.Throws<ArgumentException>(
            (Action)(() => _ = new VerificationQuery(
                factory, [], goal, [variable, variable])));
        Assert.Throws<ArgumentException>(
            (Action)(() => _ = new VerificationQuery(factory, [], goal, [foreign])));
    }

    [Test]
    public async Task SpuriousOrIncompleteModelsAbstain()
    {
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
    }

    [TestCase(
        ProofDiagnosticKind.Postcondition,
        AbstentionReason.PostconditionMayBeUndefined)]
    [TestCase(
        ProofDiagnosticKind.InternalConsistency,
        AbstentionReason.InternalConsistencyMayBeUndefined)]
    public async Task UndefinedGoalIsTypedSeparatelyFromReplayFailure(
        ProofDiagnosticKind diagnosticKind,
        AbstentionReason expectedReason)
    {
        var factory = new IrFactory();
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var predicate = factory.Binary(IrBinaryOperator.Equal,
            factory.Binary(IrBinaryOperator.Divide,
                factory.Integer(0), factory.Variable(divisor)),
            factory.Integer(0));
        var query = new VerificationQuery(factory, [],
            new Goal(factory, predicate, diagnosticKind, new SourceLocationId(0)),
            [divisor]);
        var model = new BackendModel([
            KeyValuePair.Create(divisor, factory.CreateIntegerValue(0))
        ]);

        var outcome = await new ProofKernel(
            new StubBackend(BackendCheckResult.Satisfiable(model))).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason,
            Is.EqualTo(expectedReason));
    }

    [Test]
    public async Task OpaqueEvidenceCannotValidateARefutation()
    {
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
    public async Task BackendFailuresStayTypedAndUncacheable()
    {
        var fixture = CreateFixture();
        foreach (var pair in new[] {
                     (BackendFailureReason.UnsupportedEncoding, AbstentionReason.UnsupportedEncoding),
                     (BackendFailureReason.ResourceLimit, AbstentionReason.ResourceLimit),
                     (BackendFailureReason.Timeout, AbstentionReason.Timeout),
                     (BackendFailureReason.Unavailable, AbstentionReason.BackendUnavailable),
                     (BackendFailureReason.InfrastructureFailure, AbstentionReason.InfrastructureFailure)
                 })
        {
            var outcome = await new ProofKernel(
                new StubBackend(BackendCheckResult.Unknown(pair.Item1)))
                .VerifyAsync(fixture.Query);
            Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(pair.Item2));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task BackendExceptionsBecomeTypedInfrastructureFailures(
        bool throwSynchronously)
    {
        var fixture = CreateFixture();

        var outcome = await new ProofKernel(
                new ThrowingBackend(throwSynchronously))
            .VerifyAsync(fixture.Query);

        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.InfrastructureFailure));
        Assert.That(outcome is ProvenOutcome or RefutedOutcome, Is.False);
    }

    [Test]
    public async Task MalformedUnsatCoreCannotCreateAProof()
    {
        var fixture = CreateFixture();
        var outcome = await new ProofKernel(
            new StubBackend(BackendCheckResult.Unsatisfiable([4])))
            .VerifyAsync(fixture.Query);

        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.MalformedBackendResult));
    }

    [Test]
    public void CancellationPropagatesInsteadOfBecomingSemanticUnknown()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> action = () => new ProofKernel(
                new StubBackend(BackendCheckResult.Unsatisfiable([])))
            .VerifyAsync(fixture.Query, cancellation.Token);

        Assert.ThrowsAsync<OperationCanceledException>(action);
    }

    private static Fixture CreateFixture()
    {
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

    private sealed class StubBackend(BackendCheckResult result) : ISmtBackend
    {
        private readonly BackendCheckResult _result = result;

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingBackend(bool throwSynchronously) : ISmtBackend
    {
        private readonly bool _throwSynchronously = throwSynchronously;

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_throwSynchronously)
            {
                throw new InvalidOperationException("Synchronous backend failure.");
            }

            return Task.FromException<BackendCheckResult>(
                new InvalidOperationException("Asynchronous backend failure."));
        }
    }

    private sealed class Fixture(
        IrFactory factory,
        IrVarId variable,
        OperationId operation,
        Goal goal,
        VerificationQuery query)
    {
        internal IrFactory Factory { get; } = factory;
        internal IrVarId Variable { get; } = variable;
        internal OperationId Operation { get; } = operation;
        internal Goal Goal { get; } = goal;
        internal VerificationQuery Query { get; } = query;
    }
}
