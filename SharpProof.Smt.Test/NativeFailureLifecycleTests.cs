using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeFailureLifecycleTests
{
    [Test]
    public async Task CancellationDuringInvalidNativeConfigurationPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var runner = new SmtNativeRunner(static () => new Context(), retireAfterFailure: true);

        Assert.ThrowsAsync<OperationCanceledException>((Func<Task>)(async () => await runner.CheckAsync(() =>
        {
            cancellation.Cancel();
            using var invalidSort = runner.Context.MkBitVecSort(0);
            return BackendCheckResult.Unsatisfiable([]);
        }, cancellation.Token)));
        var later = await runner.CheckAsync(() => BackendCheckResult.Unsatisfiable([]), TestContext.CurrentContext.CancellationToken);
        Assert.That(later.FailureReason, Is.EqualTo(BackendFailureReason.Unavailable));
    }

    [Test]
    public async Task ActualNativeTimeoutRetiresThePersistentRunner()
    {
        using var runner = new SmtNativeRunner(static () => new Context(), retireAfterFailure: true);
        using var solver = runner.Context.MkSolver();
        using var owner = new Z3ExpressionOwner();
        const int holes = 20;
        var placements = new BoolExpr[holes + 1][];
        for (var pigeon = 0; pigeon <= holes; pigeon++)
        {
            placements[pigeon] = new BoolExpr[holes];
            for (var hole = 0; hole < holes; hole++)
            {
                placements[pigeon][hole] = owner.Own(runner.Context.MkBoolConst($"p{pigeon}h{hole}"));
            }
            solver.Assert(owner.Own(runner.Context.MkOr(
                Enumerable.Range(0, holes).Select(hole => placements[pigeon][hole]).ToArray())));
        }
        for (var hole = 0; hole < holes; hole++)
        {
            for (var first = 0; first <= holes; first++)
            {
                for (var second = first + 1; second <= holes; second++)
                {
                    solver.Assert(owner.Own(runner.Context.MkNot(owner.Own(runner.Context.MkAnd(
                        placements[first][hole], placements[second][hole])))));
                }
            }
        }
        using var parameters = runner.Context.MkParams();
        parameters.Add("timeout", 1u);
        solver.Parameters = parameters;

        var timedOut = await runner.CheckAsync(() =>
        {
            Assert.That(solver.Check(), Is.EqualTo(Status.UNKNOWN));
            return BackendCheckResult.Unknown(SmtNativeUtilities.ClassifyUnknown(solver.ReasonUnknown));
        }, TestContext.CurrentContext.CancellationToken);
        Assert.That(timedOut.FailureReason, Is.EqualTo(BackendFailureReason.Timeout));

        var later = await runner.CheckAsync(() => BackendCheckResult.Unsatisfiable([]), TestContext.CurrentContext.CancellationToken);
        Assert.That(later.FailureReason, Is.EqualTo(BackendFailureReason.Unavailable));
    }
}
