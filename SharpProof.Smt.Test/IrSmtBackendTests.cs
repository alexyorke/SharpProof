namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class IrSmtBackendTests
{
    [Test]
    public async Task UnsatProofReturnsAHygienicCore()
    {
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
    public async Task SatModelMustReplayBeforeRefutation()
    {
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
    public async Task StrictComparisonDoesNotAcceptEqualityBoundary()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var operation = factory.CreateOperation("equal to zero");
        var equalToZero = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Variable(variable),
            factory.Integer(0));
        var strictlyNegative = factory.Binary(
            IrBinaryOperator.LessThan,
            factory.Variable(variable),
            factory.Integer(0));
        var query = new VerificationQuery(
            factory,
            [new Assumption(
                factory,
                equalToZero,
                new LoweredJustification(operation))],
            new Goal(
                factory,
                strictlyNegative,
                ProofDiagnosticKind.Postcondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(
            ((RefutedOutcome)outcome).Model.Assignments[variable].Integer,
            Is.Zero);
    }

    [Test]
    public async Task FormulaAndExplicitVariablesProduceOneExactModelSet()
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
    public async Task NormalCompletionGuardsCheckedDivision()
    {
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
    public async Task SignedRemainderOverflowProducesTypedUnknown()
    {
        var factory = new IrFactory();
        var dividend = factory.CreateVariable("dividend", factory.IntegerType);
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var dividendIsMinimum = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Variable(dividend),
            factory.Integer(long.MinValue));
        var divisorIsNegativeOne = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Variable(divisor),
            factory.Integer(-1));
        var remainder = factory.Binary(
            IrBinaryOperator.Remainder,
            factory.Variable(dividend),
            factory.Variable(divisor));
        var query = new VerificationQuery(
            factory,
            [
                new Assumption(
                    factory,
                    dividendIsMinimum,
                    new LoweredJustification(factory.CreateOperation("minimum"))),
                new Assumption(
                    factory,
                    divisorIsNegativeOne,
                    new LoweredJustification(factory.CreateOperation("negative-one")))
            ],
            new Goal(
                factory,
                factory.Binary(
                    IrBinaryOperator.Equal,
                    remainder,
                    factory.Integer(0)),
                ProofDiagnosticKind.Postcondition,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(
            ((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.PostconditionMayBeUndefined));
    }

    [TestCase(-7L, 3L, -2L, -1L)]
    [TestCase(7L, -3L, -2L, 1L)]
    [TestCase(-7L, -3L, 2L, -1L)]
    public async Task SignedDivisionAndRemainderRoundTowardZero(
        long dividendValue,
        long divisorValue,
        long expectedQuotient,
        long expectedRemainder)
    {
        var factory = new IrFactory();
        var dividend = factory.CreateVariable("dividend", factory.IntegerType);
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var quotient = factory.Binary(
            IrBinaryOperator.Divide,
            factory.Variable(dividend),
            factory.Variable(divisor));
        var remainder = factory.Binary(
            IrBinaryOperator.Remainder,
            factory.Variable(dividend),
            factory.Variable(divisor));
        var goal = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Binary(
                IrBinaryOperator.Equal,
                quotient,
                factory.Integer(expectedQuotient)),
            factory.Binary(
                IrBinaryOperator.Equal,
                remainder,
                factory.Integer(expectedRemainder)));
        var query = new VerificationQuery(
            factory,
            [
                new Assumption(
                    factory,
                    factory.Binary(
                        IrBinaryOperator.Equal,
                        factory.Variable(dividend),
                        factory.Integer(dividendValue)),
                    new LoweredJustification(factory.CreateOperation("dividend"))),
                new Assumption(
                    factory,
                    factory.Binary(
                        IrBinaryOperator.Equal,
                        factory.Variable(divisor),
                        factory.Integer(divisorValue)),
                    new LoweredJustification(factory.CreateOperation("divisor")))
            ],
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
    public async Task UndefinedGoalStateProducesTypedUnknown()
    {
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
            Is.EqualTo(AbstentionReason.PostconditionMayBeUndefined));
    }

    [Test]
    public async Task StringVariablesFailClosedWithoutNullTagEncoding()
    {
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
    public async Task NullableStringConcatCannotProduceAFalseProof()
    {
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
    public async Task OpaqueTermsFailClosed()
    {
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
    public void PreCancelledChecksDoNotBecomeUnknown()
    {
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

    [Test]
    public void NativeUnknownReasonsAreClassifiedPrecisely()
    {
        var classify = typeof(IrSmtBackend).GetMethod(
            "ClassifyUnknown",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        Assert.That(classify, Is.Not.Null);

        Assert.That(
            classify!.Invoke(null, ["timeout"]),
            Is.EqualTo(BackendFailureReason.Timeout));
        Assert.That(
            classify.Invoke(null, ["resource limit"]),
            Is.EqualTo(BackendFailureReason.ResourceLimit));
        Assert.That(
            classify.Invoke(null, ["opaque backend failure"]),
            Is.EqualTo(BackendFailureReason.InfrastructureFailure));
    }

    [Test]
    public void ActiveCancellationInterruptsTheNativeContext()
    {
        var factory = new IrFactory();
        var operation = factory.CreateOperation("repeated");
        var assumption = new Assumption(
            factory,
            factory.Boolean(true),
            new LoweredJustification(operation));
        var query = new VerificationQuery(
            factory,
            Enumerable.Repeat(assumption, 20_000),
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var backend = new IrSmtBackend();
        using var cancellation = new CancellationTokenSource();
        var gate = typeof(IrSmtBackend).GetField(
                "_gate",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?
            .GetValue(backend);
        Assert.That(gate, Is.Not.Null);

        var check = backend.CheckAsync(query, cancellation.Token);
        var entered = SpinWait.SpinUntil(
            () => IsMonitorHeld(gate!),
            TimeSpan.FromSeconds(5));
        Assert.That(entered, Is.True);
        Thread.Sleep(10);
        cancellation.Cancel();

        Func<Task> action = async () => await check;
        Assert.ThrowsAsync<OperationCanceledException>(action);
        var retired = backend.CheckAsync(query, CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.That(retired.FailureReason, Is.EqualTo(BackendFailureReason.Unavailable));
    }

    [Test]
    public void CancellationWhileQueuedAtTheBackendGateDoesNotRunTheQuery()
    {
        var factory = new IrFactory();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Queued",
            factory.BooleanType,
            isStatic: true);
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.PureOpaque(member, receiver: null),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var backend = new IrSmtBackend();
        using var cancellation = new CancellationTokenSource();
        var gate = typeof(IrSmtBackend).GetField(
                "_gate",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?
            .GetValue(backend);
        Assert.That(gate, Is.Not.Null);
        var activeChecks = typeof(IrSmtBackend).GetField(
            "_activeCheckCount",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.That(activeChecks, Is.Not.Null);

        Task<BackendCheckResult> check;
        lock (gate!)
        {
            check = backend.CheckAsync(query, cancellation.Token);
            Assert.That(
                SpinWait.SpinUntil(
                    () => (int)activeChecks!.GetValue(backend)! == 1,
                    TimeSpan.FromSeconds(5)),
                Is.True);
            cancellation.Cancel();
        }

        Func<Task> action = async () => await check;
        Assert.That(
            Assert.CatchAsync(action),
            Is.InstanceOf<OperationCanceledException>());

        var healthyQuery = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        var healthy = backend.CheckAsync(healthyQuery, CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.That(healthy.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
    }

    [Test]
    public async Task UnsupportedModelVariablesAreRejectedBeforeEncoding()
    {
        var factory = new IrFactory();
        var boolean = factory.CreateVariable("boolean", factory.BooleanType);
        var text = factory.CreateVariable("text", factory.StringType);
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Variable(boolean),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)),
            [boolean, text]);
        using var backend = new IrSmtBackend();

        var result = await backend.CheckAsync(query, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Unknown));
        Assert.That(
            result.FailureReason,
            Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
    }

    [Test]
    public async Task PublicBackendBoundsRecursiveEncodingDepth()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("deep", factory.BooleanType);
        var atBoundary = NestNot(factory, factory.Variable(variable), 255);
        var beyondBoundary = NestNot(factory, factory.Variable(variable), 256);
        using var backend = new IrSmtBackend();

        var supported = await backend.CheckAsync(
            Query(factory, variable, atBoundary), CancellationToken.None);
        var unsupported = await backend.CheckAsync(
            Query(factory, variable, beyondBoundary), CancellationToken.None);

        Assert.That(supported.Status, Is.Not.EqualTo(BackendCheckStatus.Unknown));
        Assert.That(unsupported.Status, Is.EqualTo(BackendCheckStatus.Unknown));
        Assert.That(
            unsupported.FailureReason,
            Is.EqualTo(BackendFailureReason.UnsupportedEncoding));

        static IrTerm NestNot(IrFactory factory, IrTerm term, int count)
        {
            for (var index = 0; index < count; index++)
            {
                term = factory.Unary(IrUnaryOperator.Not, term);
            }
            return term;
        }

        static VerificationQuery Query(
            IrFactory factory,
            ScopedIrId<IrVariableTag> variable,
            IrTerm goal)
        {
            return new VerificationQuery(
                factory,
                [],
                new Goal(
                    factory,
                    goal,
                    ProofDiagnosticKind.InternalConsistency,
                    new SourceLocationId(0)),
                [variable]);
        }
    }

    private static bool IsMonitorHeld(object gate)
    {
        if (!Monitor.TryEnter(gate))
        {
            return true;
        }

        Monitor.Exit(gate);
        return false;
    }
}
