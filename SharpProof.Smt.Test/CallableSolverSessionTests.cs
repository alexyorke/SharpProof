using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Microsoft.Z3;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CallableSolverSessionTests
{
    [TestCase(32, true, 2147483647UL, 2147483648UL)]
    [TestCase(64, false, ulong.MaxValue, 0UL)]
    public async Task ArithmeticWrapsAtTheDeclaredWidth(int width, bool isSigned, ulong input, ulong expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var x = factory.CreateVariable("x", type);
        var term = factory.Binary(IrBinaryOperator.Add, factory.Variable(x), factory.IntegerBits(type, 1));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(Query(factory,
            [Assume(factory, Equal(factory, factory.Variable(x), factory.IntegerBits(type, input)))],
            Equal(factory, term, factory.IntegerBits(type, expected))));
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(session.ConsumedResourceCount, Is.GreaterThan(0));
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public async Task SignedAndUnsignedComparisonsUseTheirOwnOrdering(bool isSigned, bool less)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, isSigned);
        var x = factory.CreateVariable("x", type);
        var predicate = factory.Binary(IrBinaryOperator.LessThan, factory.Variable(x), factory.IntegerBits(type, 127));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var result = await session.CheckAsync(Query(factory,
            [Assume(factory, Equal(factory, factory.Variable(x), factory.IntegerBits(type, 128)))],
            Equal(factory, predicate, factory.Boolean(less))), CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
    }

    [TestCase(8, true, 255UL, 64, false, ulong.MaxValue)]
    [TestCase(8, false, 255UL, 32, true, 255UL)]
    [TestCase(64, false, ulong.MaxValue, 8, true, 255UL)]
    [TestCase(32, true, 2147483648UL, 32, false, 2147483648UL)]
    public async Task CastsExtendBySourceSignednessAndTruncateBits(int sourceWidth, bool sourceSigned, ulong input,
        int targetWidth, bool targetSigned, ulong expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var source = factory.GetOrCreateIntegerType(sourceWidth, sourceSigned);
        var target = factory.GetOrCreateIntegerType(targetWidth, targetSigned);
        var x = factory.CreateVariable("x", source);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(Query(factory,
            [Assume(factory, Equal(factory, factory.Variable(x), factory.IntegerBits(source, input)))],
            Equal(factory, factory.Cast(target, factory.Variable(x)), factory.IntegerBits(target, expected))));
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task InactiveBodyAssumptionsAndPreviousGoalsDoNotLeakIntoLaterQueries()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var positive = Assume(factory, factory.Variable(flag));
        var negative = Assume(factory, factory.Unary(IrUnaryOperator.Not, factory.Variable(flag)));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var contradictory = await session.CheckAsync(Query(factory, [positive, negative], factory.Boolean(false)), CancellationToken.None);
        Assert.That(contradictory.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
        int[] contradictoryIndices = [0, 1];
        Assert.That(contradictory.UnsatCore, Is.EquivalentTo(contradictoryIndices));
        var feasible = await session.CheckAsync(Query(factory, [positive], factory.Boolean(false)), CancellationToken.None);
        Assert.That(feasible.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        Assert.That(feasible.Model!.Assignments[flag].Boolean, Is.True);
        var first = Query(factory, [], factory.Variable(flag));
        var second = Query(factory, [], factory.Unary(IrUnaryOperator.Not, factory.Variable(flag)));
        foreach (var query in new[] { first, second, first })
        {
            var result = await session.CheckAsync(query, CancellationToken.None);
            Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
            Assert.That(new IrInterpreter(factory).Evaluate(query.Goal.Predicate, result.Model!.Assignments).Value!.Boolean, Is.False);
        }
    }

    [Test]
    public async Task CoreIndicesReferToCurrentAssumptionOrderAndDistinctEvidence()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var positive = Assume(factory, factory.Variable(flag));
        var otherPositive = Assume(factory, factory.Variable(flag));
        var irrelevant = Assume(factory, factory.Boolean(true));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var first = await session.CheckAsync(Query(factory, [irrelevant, positive], factory.Variable(flag)), CancellationToken.None);
        int[] secondIndex = [1];
        int[] firstIndex = [0];
        Assert.That(first.UnsatCore, Is.EqualTo(secondIndex));
        var reordered = await session.CheckAsync(Query(factory, [positive, irrelevant], factory.Variable(flag)), CancellationToken.None);
        Assert.That(reordered.UnsatCore, Is.EqualTo(firstIndex));
        var differentEvidence = await session.CheckAsync(Query(factory, [irrelevant, otherPositive], factory.Variable(flag)), CancellationToken.None);
        Assert.That(differentEvidence.UnsatCore, Is.EqualTo(secondIndex));
        var trueGoal = await session.CheckAsync(Query(factory, [], factory.Boolean(true)), CancellationToken.None);
        Assert.That(trueGoal.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
        Assert.That(trueGoal.UnsatCore, Is.Empty);
    }

    [Test]
    public async Task ModelProjectionExcludesPreviousVariablesAndCompletesExplicitUnusedInputs()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var old = factory.CreateVariable("old", factory.BooleanType);
        var current = factory.CreateVariable("current", factory.GetOrCreateIntegerType(64, false));
        var unusedBoolean = factory.CreateVariable("unused-boolean", factory.BooleanType);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        await session.CheckAsync(Query(factory, [], factory.Variable(old)), CancellationToken.None);
        var result = await session.CheckAsync(Query(factory, [], factory.Boolean(false), [current, unusedBoolean]), CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        Assert.That(result.Model!.Assignments.Keys, Is.EquivalentTo(new[] { current, unusedBoolean }));
        Assert.That(result.Model.Assignments[current].Type, Is.EqualTo(factory.GetVariableInfo(current).Type));
        Assert.That(result.Model.Assignments[unusedBoolean].Kind, Is.EqualTo(IrValueKind.Boolean));
    }

    [Test]
    public async Task Unsigned64ModelReplaysBoundInputThroughTheActualOriginalCall()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(64, false);
        var input = factory.CreateVariable("input", type);
        var bodyInput = factory.CreateVariable("body-input", type);
        var result = factory.CreateVariable("result", type);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Identity", type, true, [type]);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Input, bodyInput);
        var callInstruction = builder.Call(entry, factory.CreateOperation(), result, member, null, factory.Variable(bodyInput));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(result));
        var calls = 0;
        var replay = new CallableReplayContext(builder.Build(), false,
            ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(bodyInput, input),
            ImmutableDictionary<IrVarId, IrVarId?>.Empty, [result],
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(result), factory.Variable(input)),
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty.Add(input, (0, ulong.MaxValue)), 100, ImmutableHashSet.Create(callInstruction.Id),
            postconditionGuard: factory.Boolean(true),
            replayOptions: new IrProgramReplayOptions(_ => throw new InvalidOperationException("External input substitution is forbidden.")),
            callHost: (call, _, arguments) =>
            {
                Assert.That(call.Member, Is.EqualTo(member));
                Assert.That(arguments.Single().IntegerBits, Is.EqualTo(ulong.MaxValue));
                calls++;
                return arguments[0];
            });
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyCallableAsync(Query(factory,
            [Assume(factory, Equal(factory, factory.Variable(input), factory.IntegerBits(type, ulong.MaxValue)))],
            factory.Boolean(false), [input]), replay);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(((RefutedOutcome)outcome).Model.Assignments[input].IntegerNumericValue, Is.EqualTo(new BigInteger(ulong.MaxValue)));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task UnsupportedFactoriesAndDomainsFailClosed()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.Throws<ArgumentException>((Action)(() => new CallableSolverSession(new IrFactory(), new IrSmtBackendOptions())));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var foreign = new IrFactory(IrExecutionSemantics.Total);
        var result = await session.CheckAsync(Query(foreign, [], foreign.Boolean(false)), CancellationToken.None);
        Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
        var text = factory.CreateVariable("text", factory.StringType);
        result = await session.CheckAsync(Query(factory, [], factory.Boolean(false), [text]), CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        Assert.That(result.Model!.Assignments[text].Type, Is.EqualTo(factory.StringType));
    }

    [Test]
    public async Task UnsupportedOpaqueGoalsAndAssumptionsCannotContaminateLaterQueries()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "Unknown", factory.BooleanType, isStatic: true);
        var opaque = factory.PureOpaque(member, receiver: null);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var positive = Assume(factory, factory.Variable(flag));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var goal = await session.CheckAsync(Query(factory, [positive], opaque), CancellationToken.None);
        Assert.That(goal.FailureReason, Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
        var assumption = await session.CheckAsync(Query(factory, [Assume(factory, opaque)], factory.Boolean(true)), CancellationToken.None);
        Assert.That(assumption.FailureReason, Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
        var kernel = new ProofKernel(session);
        Assert.That(await kernel.VerifyAsync(Query(factory, [positive], factory.Variable(flag))), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [], factory.Variable(flag))), Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public void CoreDecoderRejectsInactiveSelectorsAndDisposesEveryWrapper()
    {
        var active = new Dictionary<string, int>(StringComparer.Ordinal) { ["a0"] = 2 };
        var valid = new[] { new CoreLabel("g-current"), new CoreLabel("a0") };
        var result = CallableSolverSession.DecodeCore(valid, active, "g-current",
            new SmtQueryResourceMeter(100, CancellationToken.None));
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
        Assert.That(result.UnsatCore.Single(), Is.EqualTo(2));
        Assert.That(valid.All(label => label.IsDisposed), Is.True);
        foreach (var inactive in new[] { "a-inactive", "g-previous" })
        {
            var malformed = new[] { new CoreLabel(inactive), new CoreLabel("a0") };
            result = CallableSolverSession.DecodeCore(malformed, active, "g-current",
                new SmtQueryResourceMeter(100, CancellationToken.None));
            Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.MalformedResult));
            Assert.That(malformed.All(label => label.IsDisposed), Is.True);
        }
    }

    [Test]
    public async Task CachedTermsAreStillDepthValidatedBeforeAReusedCheck()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("deep", factory.BooleanType);
        IrTerm term = factory.Variable(variable);
        for (var index = 0; index < 255; index++)
        {
            term = factory.Unary(IrUnaryOperator.Not, term);
        }
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var valid = Query(factory, [], term);
        Assert.That((await session.CheckAsync(valid, CancellationToken.None)).Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var tooDeep = Query(factory, [], factory.Unary(IrUnaryOperator.Not, term));
        Assert.That((await session.CheckAsync(tooDeep, CancellationToken.None)).FailureReason,
            Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
        Assert.That((await session.CheckAsync(valid, CancellationToken.None)).Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
    }

    [Test]
    public async Task BooleanAndConditionalTermsKeepSelectedTypedValues()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var x = factory.CreateVariable("x", factory.IntegerType);
        var condition = factory.Binary(IrBinaryOperator.OrElse,
            factory.Binary(IrBinaryOperator.AndAlso, factory.Variable(flag), factory.Boolean(true)),
            factory.Unary(IrUnaryOperator.Not, factory.Variable(flag)));
        var arithmetic = factory.Binary(IrBinaryOperator.Multiply,
            factory.Unary(IrUnaryOperator.Negate, factory.Variable(x)),
            factory.Binary(IrBinaryOperator.Subtract, factory.Integer(5), factory.Variable(x)));
        var value = factory.Conditional(condition, arithmetic, factory.Integer(99));
        var predicate = factory.Binary(IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.LessThanOrEqual, value, factory.Integer(-6)),
            factory.Binary(IrBinaryOperator.GreaterThan, value, factory.Integer(-7)));
        predicate = factory.Binary(IrBinaryOperator.AndAlso, predicate,
            factory.Binary(IrBinaryOperator.GreaterThanOrEqual, value, factory.Integer(-6)));
        predicate = factory.Binary(IrBinaryOperator.AndAlso, predicate,
            factory.Binary(IrBinaryOperator.NotEqual, value, factory.Integer(0)));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(Query(factory,
            [Assume(factory, Equal(factory, factory.Variable(x), factory.Integer(2)))], predicate));
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase(true, 1UL)]
    [TestCase(false, ulong.MaxValue)]
    public async Task TypedConditionalCounterexamplesKeepTheActualBooleanModelAndSelectedArm(bool selected, ulong expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(64, false);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var value = factory.Conditional(factory.Variable(flag), factory.IntegerBits(type, 1), factory.IntegerBits(type, ulong.MaxValue));
        var query = Query(factory, [Assume(factory, Equal(factory, factory.Variable(flag), factory.Boolean(selected)))],
            Equal(factory, value, factory.IntegerBits(type, selected ? ulong.MaxValue : 1)));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var model = ((RefutedOutcome)outcome).Model.Assignments;
        Assert.That(model[flag].Boolean, Is.EqualTo(selected));
        Assert.That(new IrInterpreter(factory).Evaluate(value, model).Value!.IntegerBits, Is.EqualTo(expected));
    }

    [TestCase(1U)]
    [TestCase(3U)]
    public async Task ResourceLimitsChargeSetupAndRepeatedChecksUseTheirOwnToken(uint limit)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var query = Query(factory, [], factory.Boolean(false));
        using var limited = new CallableSolverSession(factory, new IrSmtBackendOptions(limit));
        var exhausted = await limited.CheckAsync(query, CancellationToken.None);
        Assert.That(exhausted.FailureReason, Is.EqualTo(BackendFailureReason.ResourceLimit));
        Assert.That(limited.ConsumedResourceCount, Is.EqualTo(limit));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        using var cancellation = new CancellationTokenSource();
        Assert.That((await session.CheckAsync(query, cancellation.Token)).Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        await cancellation.CancelAsync();
        var before = session.ConsumedResourceCount;
        Assert.That((await session.CheckAsync(query, CancellationToken.None)).Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        Assert.That(session.ConsumedResourceCount, Is.GreaterThan(before));
    }

    [Test]
    public void WarmEncoderStillChargesEveryExplicitModelVariableVisit()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variables = Enumerable.Range(0, 10).Select(index => factory.CreateVariable("v" + index, factory.BooleanType)).ToImmutableArray();
        var query = Query(factory, [], factory.Boolean(true), variables);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        encoder.ValidateQuery(query, new SmtQueryResourceMeter(100, CancellationToken.None), CancellationToken.None);
        Assert.Throws<SmtResourceLimitException>((Action)(() => encoder.ValidateQuery(query,
            new SmtQueryResourceMeter(2, CancellationToken.None), CancellationToken.None)));
    }

    [Test]
    public async Task QueuedCancellationCannotInterruptOrRetireAnotherActiveQuery()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var runner = GetRunner(session);
        var gate = typeof(SmtNativeRunner).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runner)!;
        using var cancellation = new CancellationTokenSource();
        Task<BackendCheckResult> active;
        Task<BackendCheckResult> queued;
        Task cancellationTask;
        lock (gate)
        {
            active = session.CheckAsync(Query(factory, [], factory.Boolean(true)), CancellationToken.None);
            queued = session.CheckAsync(Query(factory, [], factory.Boolean(false)), cancellation.Token);
            cancellationTask = cancellation.CancelAsync();
            Assert.That(SpinWait.SpinUntil(() => queued.IsCompleted, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(queued.IsCanceled, Is.True);
        }
        await cancellationTask;
        Assert.That((await active).Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
        Assert.That((await session.CheckAsync(Query(factory, [], factory.Boolean(false)), CancellationToken.None)).Status,
            Is.EqualTo(BackendCheckStatus.Satisfiable));
    }

    [Test]
    public async Task ActiveCancellationRetiresOnlyThatSessionAndDisposalDrainsQueuedChecks()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        using var cancellation = new CancellationTokenSource();
        var runner = GetRunner(session);
        using var entered = new ManualResetEventSlim();
        var check = runner.CheckAsync(() =>
        {
            entered.Set();
            Assert.That(cancellation.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)), Is.True);
            cancellation.Token.ThrowIfCancellationRequested();
            return BackendCheckResult.Unsatisfiable([]);
        }, cancellation.Token);
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        await cancellation.CancelAsync();
        Func<Task> canceledCheck = async () => await check;
        Assert.CatchAsync<OperationCanceledException>(canceledCheck);
        Assert.That((await session.CheckAsync(Query(factory, [], factory.Boolean(false)), CancellationToken.None)).FailureReason,
            Is.EqualTo(BackendFailureReason.Unavailable));
        using var fresh = new CallableSolverSession(factory, new IrSmtBackendOptions());
        Assert.That((await fresh.CheckAsync(Query(factory, [], factory.Boolean(false)), CancellationToken.None)).Status,
            Is.EqualTo(BackendCheckStatus.Satisfiable));
        var freshRunner = GetRunner(fresh);
        var queryGate = (SemaphoreSlim)typeof(SmtNativeRunner).GetField("_queryGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(freshRunner)!;
        var disposeStarted = typeof(SmtNativeRunner).GetField("_disposeStarted", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await queryGate.WaitAsync();
        var queued = fresh.CheckAsync(Query(factory, [], factory.Boolean(false)), CancellationToken.None);
        var disposal = Task.Run(fresh.Dispose);
        try
        {
            Assert.That(SpinWait.SpinUntil(() => (int)disposeStarted.GetValue(freshRunner)! != 0, TimeSpan.FromSeconds(5)), Is.True);
        }
        finally
        {
            queryGate.Release();
        }
        Assert.That((await queued.WaitAsync(TimeSpan.FromSeconds(5))).FailureReason, Is.EqualTo(BackendFailureReason.Unavailable));
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DisposalReleasesTheSolverAndAllCachedNativeExpressionsBeforeContextTeardown()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.IntegerType);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        Assert.That((await session.CheckAsync(Query(factory,
            [Assume(factory, Equal(factory, factory.Variable(x), factory.Integer(2)))], factory.Boolean(false)),
            CancellationToken.None)).Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var solver = (Solver)typeof(CallableSolverSession).GetField("_solver", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var owner = (Z3ExpressionOwner)typeof(CallableSolverSession).GetField("_owner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var expressions = ((List<Expr>)typeof(Z3ExpressionOwner).GetField("_expressions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!).ToArray();
        Assert.That(expressions, Is.Not.Empty);
        Assert.That(NativeObject(solver), Is.Not.EqualTo(IntPtr.Zero));
        Assert.That(expressions.All(expression => NativeObject(expression) != IntPtr.Zero), Is.True);
        session.Dispose();
        Assert.That(NativeObject(solver), Is.EqualTo(IntPtr.Zero));
        Assert.That(expressions.All(expression => NativeObject(expression) == IntPtr.Zero), Is.True);
        Assert.That((await session.CheckAsync(Query(factory, [], factory.Boolean(false)), CancellationToken.None)).FailureReason,
            Is.EqualTo(BackendFailureReason.Unavailable));
    }

    [Test]
    public async Task NativeFailureAfterAnAssertionRetiresTheCandidateButKeepsLegacyRunnerReusable()
    {
        foreach (var candidate in new[] { true, false })
        {
            using var runner = new SmtNativeRunner(static () => new Context(), retireAfterFailure: candidate);
            using var solver = runner.Context.MkSolver();
            var failure = await runner.CheckAsync(() =>
            {
                using var predicate = runner.Context.MkTrue();
                solver.Assert(predicate);
                throw new InvalidOperationException("Native selector bookkeeping did not complete.");
            }, CancellationToken.None);
            Assert.That(failure.FailureReason, Is.EqualTo(BackendFailureReason.InfrastructureFailure));
            var later = await runner.CheckAsync(() =>
            {
                Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
                return BackendCheckResult.Satisfiable(new BackendModel([]));
            }, CancellationToken.None);
            Assert.That(later.Status, Is.EqualTo(candidate ? BackendCheckStatus.Unknown : BackendCheckStatus.Satisfiable));
            Assert.That(later.FailureReason, Is.EqualTo(candidate ? BackendFailureReason.Unavailable : BackendFailureReason.None));
        }
    }

    private static IntPtr NativeObject(Z3Object value)
    {
        return (IntPtr)typeof(Z3Object).GetProperty("NativeObject", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(value)!;
    }

    private static SmtNativeRunner GetRunner(CallableSolverSession session)
    {
        return (SmtNativeRunner)typeof(CallableSolverSession).GetField("_runner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
    }

    private static Assumption Assume(IrFactory factory, IrTerm predicate)
    {
        return new(factory, predicate, new LoweredJustification(factory.CreateOperation()));
    }

    private static IrTerm Equal(IrFactory factory, IrTerm left, IrTerm right)
    {
        return factory.Binary(IrBinaryOperator.Equal, left, right);
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<Assumption> assumptions, IrTerm goal,
        ImmutableArray<IrVarId> variables = default)
    {
        return new(factory, assumptions,
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), variables);
    }

    private sealed class CoreLabel(string name) : IDisposable
    {
        internal bool IsDisposed { get; private set; }
        public override string ToString() { return name; }
        public void Dispose() { IsDisposed = true; }
    }
}
