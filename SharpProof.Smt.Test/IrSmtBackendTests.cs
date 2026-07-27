namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class IrSmtBackendTests {
    [Test]
    public async Task UnsatProofReturnsAHygienicCore() {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("source name is irrelevant", factory.IntegerType);
        var operation = factory.CreateOperation("lowered");
        var lowerBound = factory.Binary(
            IrBinaryOperator.GreaterThanOrEqual,
            factory.Variable(variable),
            factory.Integer(1));
        var goal = factory.Binary(
            IrBinaryOperator.GreaterThan,
            factory.Variable(variable),
            factory.Integer(0));
        var query = new VerificationQuery(
            factory,
            [new Assumption(factory, lowerBound, new LoweredJustification(operation))],
            new Goal(
                factory,
                goal,
                ProofDiagnosticKind.Precondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(((ProvenOutcome)outcome).Core.Length, Is.EqualTo(1));
        Assert.That(
            ((LoweredJustification)((ProvenOutcome)outcome).Core[0]).Operation,
            Is.EqualTo(operation));
    }

    [Test]
    public async Task SatModelMustReplayBeforeRefutation() {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var goal = factory.Binary(
            IrBinaryOperator.GreaterThan,
            factory.Variable(variable),
            factory.Integer(0));
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                goal,
                ProofDiagnosticKind.Precondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var value = ((RefutedOutcome)outcome).Model.Assignments[variable].Integer;
        Assert.That(value, Is.LessThanOrEqualTo(0));
    }

    [Test]
    public async Task FormulaAndExplicitVariablesProduceOneExactModelSet() {
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

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(query.ModelVariables, Has.Length.EqualTo(3));
        Assert.That(query.ModelVariables[0], Is.EqualTo(integer));
        Assert.That(query.ModelVariables[1], Is.EqualTo(boolean));
        Assert.That(query.ModelVariables[2], Is.EqualTo(formula));
        var assignments = ((RefutedOutcome)outcome).Model.Assignments;
        Assert.That(assignments, Has.Count.EqualTo(3));
        Assert.That(assignments[integer].Kind, Is.EqualTo(IrValueKind.Integer));
        Assert.That(assignments[boolean].Kind, Is.EqualTo(IrValueKind.Boolean));
        Assert.That(assignments[formula].Boolean, Is.False);
    }

    [Test]
    public async Task NormalCompletionGuardsCheckedDivision() {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var operation = factory.CreateOperation("nonzero");
        var nonzero = factory.Binary(
            IrBinaryOperator.NotEqual,
            factory.Variable(variable),
            factory.Integer(0));
        var quotient = factory.Binary(
            IrBinaryOperator.Divide,
            factory.Variable(variable),
            factory.Variable(variable));
        var goal = factory.Binary(
            IrBinaryOperator.Equal,
            quotient,
            factory.Integer(1));
        var query = new VerificationQuery(
            factory,
            [new Assumption(factory, nonzero, new LoweredJustification(operation))],
            new Goal(
                factory,
                goal,
                ProofDiagnosticKind.Postcondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task UndefinedGoalStateCannotProduceAProof() {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var quotient = factory.Binary(
            IrBinaryOperator.Divide,
            factory.Integer(0),
            factory.Variable(variable));
        var goal = factory.Binary(
            IrBinaryOperator.Equal,
            quotient,
            factory.Integer(0));
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                goal,
                ProofDiagnosticKind.Postcondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    [Test]
    public async Task StringVariablesFailClosedWithoutNullTagEncoding() {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("text", factory.StringType);
        var goal = factory.Binary(
            IrBinaryOperator.GreaterThan,
            factory.Length(factory.Variable(variable)),
            factory.Integer(0));
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                goal,
                ProofDiagnosticKind.Precondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.UnsupportedEncoding));
    }

    [Test]
    public async Task NullableStringConcatCannotProduceAFalseProof() {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("text", factory.StringType);
        var concatenated = factory.Binary(
            IrBinaryOperator.StringConcat,
            factory.Variable(variable),
            factory.String(string.Empty));
        var goal = factory.Binary(
            IrBinaryOperator.Equal,
            concatenated,
            factory.Variable(variable));
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                goal,
                ProofDiagnosticKind.Postcondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.UnsupportedEncoding));
    }

    [Test]
    public async Task OpaqueTermsFailClosed() {
        var factory = new IrFactory();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Unknown",
            factory.BooleanType,
            isStatic: true);
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.PureOpaque(member, receiver: null),
                ProofDiagnosticKind.Precondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.UnsupportedEncoding));
    }

    [Test]
    public void PreCancelledChecksDoNotBecomeUnknown() {
        var factory = new IrFactory();
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var backend = new IrSmtBackend();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> action = () => backend.CheckAsync(query, cancellation.Token);

        Assert.ThrowsAsync<OperationCanceledException>(action);
    }
}
