namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class ResourceAccountingRegressionTests
{
    [Test]
    public void NativeResourceCounterWrapIsChargedAsUnsignedDelta()
    {
        Assert.That(
            SmtNativeUtilities.ComputeResourceDelta(uint.MaxValue - 2, 1),
            Is.EqualTo(4));
    }

    [Test]
    public async Task CancellationAfterNativeCompletionStillPublishesItsResourceCharge()
    {
        using var cancellation = new CancellationTokenSource();
        using var runner = new SmtNativeRunner(static () => new Microsoft.Z3.Context(), retireAfterFailure: true);
        long published = 0;
        long nativeCost = 0;
        var check = runner.CheckAsync(() =>
        {
            var meter = new SmtQueryResourceMeter(1_000_000, cancellation.Token);
            try
            {
                using var solver = runner.Context.MkSolver();
                var before = SmtNativeUtilities.ReadResourceCount(solver);
                Assert.That(solver.Check(), Is.EqualTo(Microsoft.Z3.Status.SATISFIABLE));
                var after = SmtNativeUtilities.ReadResourceCount(solver);
                Assert.That(after, Is.Not.Null);
                nativeCost = SmtNativeUtilities.ComputeResourceDelta(before.GetValueOrDefault(), after!.Value);
                Assert.That(nativeCost, Is.GreaterThan(0));
                cancellation.Cancel();
                meter.ConsumeNative(nativeCost);
                return BackendCheckResult.Unsatisfiable([]);
            }
            finally
            {
                published = meter.Consumed;
            }
        }, cancellation.Token);
        var canceled = false;
        try
        {
            await check;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        Assert.That(canceled, Is.True);
        Assert.That(published, Is.GreaterThanOrEqualTo(nativeCost));
    }

    [Test]
    public async Task NativeBudgetExhaustionRetainsCompletedWorkAndTheNextCheckGetsItsOwnBudget()
    {
        using var runner = new SmtNativeRunner(static () => new Microsoft.Z3.Context(), retireAfterFailure: true);
        using var solver = runner.Context.MkSolver();
        using var predicate = runner.Context.MkBoolConst("native-budget");
        solver.Assert(predicate);
        long published = 0;
        var exhausted = await runner.CheckAsync(() =>
        {
            var meter = new SmtQueryResourceMeter(1, CancellationToken.None);
            try
            {
                SmtNativeCheck.Run(solver, [], meter);
                return BackendCheckResult.Satisfiable(new BackendModel([]));
            }
            finally
            {
                published = meter.Consumed;
            }
        }, CancellationToken.None);
        Assert.That(exhausted.FailureReason, Is.EqualTo(BackendFailureReason.ResourceLimit));
        Assert.That(published, Is.GreaterThan(1));
        var later = await runner.CheckAsync(() =>
        {
            var meter = new SmtQueryResourceMeter(1_000_000, CancellationToken.None);
            Assert.That(SmtNativeCheck.Run(solver, [], meter), Is.EqualTo(Microsoft.Z3.Status.SATISFIABLE));
            Assert.That(meter.Consumed, Is.GreaterThan(0));
            return BackendCheckResult.Satisfiable(new BackendModel([]));
        }, CancellationToken.None);
        Assert.That(later.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
    }

    [Test]
    public async Task NativeResourceAccountingChargesOnlyTheCurrentQuery()
    {
        const uint queryLimit = 1_000_000;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var operation = factory.CreateOperation("tracked");
        var expensive = CreateTrackedQuery(factory, operation, 256);
        var inexpensive = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var backend = new CallableSolverSession(factory,
            new IrSmtBackendOptions(queryLimit));

        var first = await backend.CheckAsync(expensive, CancellationToken.None);
        var firstCost = backend.ConsumedResourceCount;
        var second = await backend.CheckAsync(inexpensive, CancellationToken.None);
        var secondCost = backend.ConsumedResourceCount - firstCost;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
            Assert.That(second.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
            Assert.That(firstCost, Is.GreaterThan(0));
            Assert.That(secondCost, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                secondCost,
                Is.LessThan(firstCost),
                "A trivial query must not be charged the prior query's native work.");
        }
    }

    [Test]
    public async Task RepeatedQueriesDoNotHitResourceLimitFromPriorQueries()
    {
        const uint queryLimit = 50_000;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var operation = factory.CreateOperation("tracked");
        var query = CreateTrackedQuery(factory, operation, 256);
        using var backend = new CallableSolverSession(factory,
            new IrSmtBackendOptions(queryLimit));

        for (var index = 0; index < 40; index++)
        {
            var result = await backend.CheckAsync(query, CancellationToken.None);

            Assert.That(
                result.Status,
                Is.EqualTo(BackendCheckStatus.Unsatisfiable),
                $"query {index} should remain within its own resource limit");
        }
    }

    private static VerificationQuery CreateTrackedQuery(
        IrFactory factory,
        ScopedIrId<IrOperationTag> operation,
        int assumptionCount)
    {
        var assumptions = Enumerable.Range(0, assumptionCount)
            .Select(index => new Assumption(
                factory,
                factory.Variable(factory.CreateVariable(
                    "tracked-" + index,
                    factory.BooleanType)),
                new LoweredJustification(operation)))
            .ToArray();

        return new VerificationQuery(
            factory,
            assumptions,
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
    }
}
