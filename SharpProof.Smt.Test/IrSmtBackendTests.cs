using Z3Context = Microsoft.Z3.Context;
using Z3Expr = Microsoft.Z3.Expr;
using Z3Object = Microsoft.Z3.Z3Object;
using Z3Status = Microsoft.Z3.Status;

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
        await AssertUnsupportedEncoding(
            factory,
            goal,
            ProofDiagnosticKind.Precondition);
    }

    [Test]
    public async Task EmbeddedNullStringFailsClosedWithoutTruncation()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("text", factory.StringType);
        var goal = factory.Binary(
            IrBinaryOperator.Equal,
            factory.String("left\0right"),
            factory.Variable(variable));
        await AssertUnsupportedEncoding(
            factory,
            goal,
            ProofDiagnosticKind.Precondition);
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
        await AssertUnsupportedEncoding(
            factory,
            goal,
            ProofDiagnosticKind.Postcondition);
    }

    private static async Task AssertUnsupportedEncoding(
        IrFactory factory,
        IrTerm goal,
        ProofDiagnosticKind diagnosticKind)
    {
        var query = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                goal,
                diagnosticKind,
                new SourceLocationId(0)));

        using var backend = new IrSmtBackend();
        var outcome = await new ProofKernel(backend).VerifyAsync(query);

        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
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
    public void NullOptionsAreRejectedBeforeContextCreation()
    {
        using var context = new Z3Context();
        var contextFactoryCalls = 0;
        Action action = () => _ = new IrSmtBackend(
            null!,
            () =>
            {
                contextFactoryCalls++;
                return context;
            });

        var exception = Assert.Throws<ArgumentNullException>(action);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("options"));
            Assert.That(contextFactoryCalls, Is.Zero);
        }
    }

    [Test]
    public void ResourceLimitSymbolIsDisposedAfterConfiguration()
    {
        using var context = new Z3Context();
        using var parameters = context.MkParams();
        var symbol = context.MkSymbol("rlimit");
        Assert.That(NativeObject(symbol), Is.Not.EqualTo(IntPtr.Zero));

        IrSmtBackend.AddOwnedParameter(parameters, symbol, 100);

        Assert.That(NativeObject(symbol), Is.EqualTo(IntPtr.Zero));
    }

    [Test]
    public async Task ResourceAccountingTreatsEachSolverSnapshotAsFresh()
    {
        const uint queryLimit = 1_000_000;
        var factory = new IrFactory();
        var operation = factory.CreateOperation("tracked");
        var assumptions = Enumerable.Range(0, 256)
            .Select(index => new Assumption(
                factory,
                factory.Variable(factory.CreateVariable(
                    "tracked-" + index,
                    factory.BooleanType)),
                new LoweredJustification(operation)))
            .ToArray();
        var expensive = new VerificationQuery(
            factory,
            assumptions,
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        var inexpensive = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var backend = new IrSmtBackend(
            new IrSmtBackendOptions(queryLimit));

        _ = await backend.CheckAsync(expensive, CancellationToken.None);
        var afterExpensive = backend.ConsumedResourceCount;
        _ = await backend.CheckAsync(inexpensive, CancellationToken.None);
        var inexpensiveCost = backend.ConsumedResourceCount - afterExpensive;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterExpensive, Is.GreaterThan(0));
            Assert.That(inexpensiveCost, Is.GreaterThanOrEqualTo(0));
            Assert.That(inexpensiveCost, Is.LessThanOrEqualTo(queryLimit));
        }
    }

    [Test]
    public void UnsatCoreWrappersAreDisposedOnSuccessAndMalformedResult()
    {
        var successful = new[]
        {
            new DisposableLabel("first"),
            new DisposableLabel("second")
        };
        var success = IrSmtBackend.CreateUnsatisfiable(
            successful,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["first"] = 2,
                ["second"] = 1
            },
            static expression => expression.Label);

        var malformed = new[]
        {
            new DisposableLabel("missing"),
            new DisposableLabel("unvisited")
        };
        var failure = IrSmtBackend.CreateUnsatisfiable(
            malformed,
            new Dictionary<string, int>(StringComparer.Ordinal),
            static expression => expression.Label);

        Assert.Multiple((Action)(() =>
        {
            Assert.That(
                success.Status,
                Is.EqualTo(BackendCheckStatus.Unsatisfiable));
            Assert.That(success.UnsatCore, Is.EqualTo((int[])[1, 2]));
            Assert.That(successful.All(static item => item.IsDisposed), Is.True);
            Assert.That(failure.Status, Is.EqualTo(BackendCheckStatus.Unknown));
            Assert.That(
                failure.FailureReason,
                Is.EqualTo(BackendFailureReason.MalformedResult));
            Assert.That(malformed.All(static item => item.IsDisposed), Is.True);
        }));
    }

    [Test]
    public void QueryExpressionOwnerDisposesPinnedZ3ExpressionsWithoutManagedGc()
    {
        Assert.That(
            typeof(Z3Context).Assembly.GetName().Version,
            Is.EqualTo(new System.Version(4, 12, 2, 0)));

        using var context = new Z3Context();
        using var solver = context.MkSolver();
        using var owner = new Z3ExpressionOwner();
        var expressions = new List<Z3Expr>();
        for (var index = 0; index < 64; index++)
        {
            var left = owner.Own(context.MkIntConst("owner-left-" + index));
            var right = owner.Own(context.MkInt(index));
            var sum = owner.Own(context.MkAdd(
                (Microsoft.Z3.ArithExpr)left,
                (Microsoft.Z3.ArithExpr)right));
            var constraint = owner.Own(
                context.MkEq(sum, right));
            expressions.Add(left);
            expressions.Add(right);
            expressions.Add(sum);
            expressions.Add(constraint);
            solver.Assert((Microsoft.Z3.BoolExpr)constraint);
        }

        Assert.That(solver.Check(), Is.EqualTo(Z3Status.SATISFIABLE));
        Assert.That(owner.OwnedCount, Is.EqualTo(expressions.Count));
        Assert.That(expressions.All(IsLiveNativeObject), Is.True);

        owner.Dispose();

        Assert.Multiple((Action)(() =>
        {
            Assert.That(owner.OwnedCount, Is.Zero);
            Assert.That(expressions.All(static expression =>
                NativeObject(expression) == IntPtr.Zero), Is.True);
            Assert.That(solver.Check(), Is.EqualTo(Z3Status.SATISFIABLE));
        }));
    }

    [Test]
    public void QueryExpressionOwnerDisposesOnExceptionalAndCanceledExit()
    {
        using var context = new Z3Context();
        AssertOwnedExpressionDisposed<InvalidOperationException>(
            context,
            static _ => { });

        using var cancellation = new CancellationTokenSource();
        AssertOwnedExpressionDisposed<OperationCanceledException>(
            context,
            expression =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            });
    }

    [Test]
    public void ActiveCancellationDoesNotPoisonTheBackend()
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
        Assert.That(
            healthy.Status,
            Is.EqualTo(BackendCheckStatus.Unsatisfiable));
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
        var healthyQuery = new VerificationQuery(
            factory,
            [],
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
        var activeChecks = typeof(IrSmtBackend).GetField(
            "_activeCheckCount",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        Assert.That(activeChecks, Is.Not.Null);

        Task<BackendCheckResult> active;
        Task<BackendCheckResult> queued;
        lock (gate!)
        {
            active = backend.CheckAsync(
                healthyQuery,
                CancellationToken.None);
            Assert.That(
                SpinWait.SpinUntil(
                    () => (int)activeChecks!.GetValue(backend)! == 1,
                    TimeSpan.FromSeconds(5)),
                Is.True);
            queued = backend.CheckAsync(query, cancellation.Token);
            Assert.That(
                SpinWait.SpinUntil(
                    () => (int)activeChecks!.GetValue(backend)! > 1,
                    TimeSpan.FromSeconds(1)),
                Is.False,
                "A queued check must not occupy another worker thread.");
            cancellation.Cancel();
            Assert.That(
                SpinWait.SpinUntil(
                    () => queued.IsCompleted,
                    TimeSpan.FromSeconds(1)),
                Is.True,
                "A canceled queued check must not wait for the active solver.");
        }

        Assert.That(
            active.GetAwaiter().GetResult().Status,
            Is.EqualTo(BackendCheckStatus.Unsatisfiable));
        Func<Task> action = async () => await queued;
        Assert.That(
            Assert.CatchAsync(action),
            Is.InstanceOf<OperationCanceledException>());

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
    public async Task ManagedModelVariableWorkIsResourceAccounted()
    {
        const int variableCount = 512;
        var query = CreateUnusedBooleanModelQuery(variableCount);
        using var backend = new IrSmtBackend();

        var result = await backend.CheckAsync(query, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Status,
                Is.EqualTo(BackendCheckStatus.Satisfiable));
            Assert.That(
                backend.ConsumedResourceCount,
                Is.GreaterThanOrEqualTo(variableCount));
        }
    }

    [Test]
    public async Task ManagedModelVariableWorkHonorsTheQueryResourceLimit()
    {
        const uint queryLimit = 100;
        var query = CreateUnusedBooleanModelQuery(512);
        using var backend = new IrSmtBackend(new IrSmtBackendOptions(queryLimit));

        var result = await backend.CheckAsync(query, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Unknown));
            Assert.That(
                result.FailureReason,
                Is.EqualTo(BackendFailureReason.ResourceLimit));
            Assert.That(backend.ConsumedResourceCount, Is.LessThanOrEqualTo(queryLimit));
        }
    }

    [Test]
    public async Task SharedAssumptionDagIsDepthValidatedOnce()
    {
        const uint queryLimit = 10_000;
        var factory = new IrFactory();
        var variable = factory.CreateVariable("shared-depth", factory.BooleanType);
        IrTerm sharedPredicate = factory.Variable(variable);
        for (var index = 0; index < 127; index++)
        {
            sharedPredicate = factory.Unary(IrUnaryOperator.Not, sharedPredicate);
        }
        var operation = factory.CreateOperation("shared-depth");
        var assumption = new Assumption(
            factory,
            sharedPredicate,
            new LoweredJustification(operation));
        var query = new VerificationQuery(
            factory,
            Enumerable.Repeat(assumption, 100),
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var backend = new IrSmtBackend(new IrSmtBackendOptions(queryLimit));

        var result = await backend.CheckAsync(query, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Status,
                Is.EqualTo(BackendCheckStatus.Unsatisfiable));
            Assert.That(backend.ConsumedResourceCount, Is.LessThan(queryLimit));
        }
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

    private static VerificationQuery CreateUnusedBooleanModelQuery(int variableCount)
    {
        var factory = new IrFactory();
        var variables = System.Collections.Immutable.ImmutableArray.CreateRange(
            Enumerable.Range(0, variableCount)
                .Select(index => factory.CreateVariable(
                    "unused-model-" + index,
                    factory.BooleanType)));
        return new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Boolean(false),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)),
            variables);
    }

    private static bool IsLiveNativeObject(Z3Expr expression)
    {
        return NativeObject(expression) != IntPtr.Zero;
    }

    private static IntPtr NativeObject(Z3Object expression)
    {
        var property = typeof(Z3Object).GetProperty(
            "NativeObject",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.That(property, Is.Not.Null);
        return (IntPtr)property!.GetValue(expression)!;
    }

    private static void ThrowAfterOwning(
        Z3Context context,
        Action<Z3Expr> afterOwn)
    {
        using var owner = new Z3ExpressionOwner();
        var expression = owner.Own(context.MkInt(7));
        afterOwn(expression);
        throw new InvalidOperationException("pinned query failure");
    }

    private static void AssertOwnedExpressionDisposed<TException>(
        Z3Context context,
        Action<Z3Expr> afterOwn)
        where TException : Exception
    {
        Z3Expr? expression = null;
        Assert.Throws<TException>(() => ThrowAfterOwning(
            context,
            owned =>
            {
                expression = owned;
                afterOwn(owned);
            }));
        Assert.That(NativeObject(expression!), Is.EqualTo(IntPtr.Zero));
    }

    private sealed class DisposableLabel(string label) : IDisposable
    {
        internal string Label { get; } = label;
        internal bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
