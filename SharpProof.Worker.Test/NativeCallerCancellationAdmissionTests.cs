using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeCallerCancellationAdmissionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CallerCancellationPrecedesNativeQueriesAndPreservesOwnedClaimRows(bool canceled)
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        var target = project.Snapshot.Callables.Single();
        var budgets = project.Request.Budgets;
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(target.Total!.Program.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        using var caller = new CancellationTokenSource();
        using var boundary = new CancellationTokenSource();
        if (canceled)
        { await caller.CancelAsync(); }
        var result = await CallableVerificationPolicy.VerifyNativeTargetAsync(session, target, budgets,
            () => session.ConsumedResourceCount, budgets.MethodWallTimeMilliseconds, boundary, caller.Token);
        Assert.That(result.Callable.CallableId, Is.EqualTo(target.Entry.CallableId));
        Assert.That(result.Claims.Select(claim => claim.ClaimId), Is.EqualTo(target.Entry.ClaimIds));
        Assert.That(result.Callable.Assumptions.Select(assumption => assumption.Id),
            Is.EqualTo(target.Entry.Assumptions.Select(assumption => assumption.Id)));
        if (canceled)
        {
            Assert.That(result.Callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(result.Callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.Canceled));
            Assert.That(result.Claims.All(claim => claim.Outcome == WorkerClaimOutcome.Unknown && claim.Reason == WorkerClaimReason.Canceled), Is.True);
            Assert.That(session.ConsumedResourceCount, Is.Zero);
        }
        else
        {
            Assert.That(result.Callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(result.Callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.None));
            Assert.That(result.Claims.Count(claim => claim.Outcome == WorkerClaimOutcome.Proven), Is.EqualTo(1));
            Assert.That(result.Claims.Count(claim => claim.Outcome == WorkerClaimOutcome.Refuted), Is.EqualTo(1));
            Assert.That(result.Claims.All(claim => claim.Reason == WorkerClaimReason.None), Is.True);
            Assert.That(session.ConsumedResourceCount, Is.GreaterThan(0));
        }
    }
}
