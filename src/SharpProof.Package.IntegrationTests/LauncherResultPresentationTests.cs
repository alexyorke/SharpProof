using NUnit.Framework;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class LauncherResultPresentationTests
{
    [Test]
    public void SummaryKeepsCoverageVacuityAndUniqueDeclaredAssumptionsVisible()
    {
        var used = new WorkerAssumptionEvidence { Id = "shared", Kind = WorkerAssumptionKind.UserAssume, Used = true };
        var unused = new WorkerAssumptionEvidence { Id = "shared", Kind = WorkerAssumptionKind.UserAssume };
        var response = new WorkerVerifyResponse
        {
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Manifest = new WorkerClaimManifest
            {
                Callables = [new(), new(), new()],
                Claims = [new(), new(), new()]
            },
            ClaimResults = [
                new() { Outcome = WorkerClaimOutcome.Proven, Vacuity = WorkerVacuityKind.ContradictoryPreconditions, Assumptions = [used] },
                new() { Outcome = WorkerClaimOutcome.Refuted },
                new() { Outcome = WorkerClaimOutcome.Unknown }],
            CallableResults = [
                new() { Coverage = WorkerCallableCoverage.Complete, Assumptions = [unused] },
                new() { Coverage = WorkerCallableCoverage.Incomplete, Assumptions = [unused] },
                new() { Coverage = WorkerCallableCoverage.Complete, Assumptions = [new() { Id = "pre", Kind = WorkerAssumptionKind.Precondition }] }],
            Summary = new() { ElapsedMilliseconds = 1234, CacheStatus = WorkerCacheStatus.Hit }
        };
        Assert.That(LauncherPresentation.HumanSummary(response), Is.EqualTo(
            "SharpProof results: run=Complete (None); selected callables=3, claims=3; Proven=1, Refuted=1, Unknown=1; incomplete callables=1, vacuous claims=1; declared user/trusted assumptions=1, used=1; elapsed=1234 ms, cache=Hit."));
    }

    [Test]
    public void EveryEmittedUnknownAndIncompleteReasonHasAdvice()
    {
        foreach (var reason in Enum.GetValues<WorkerClaimReason>().Where(static reason =>
            reason is not WorkerClaimReason.None and not WorkerClaimReason.Unspecified))
        {
            Assert.That(LauncherPresentation.UnknownGuidance(reason), Does.Contain(
                "https://github.com/alexyorke/SharpProof/blob/master/docs/unknown-reasons.md"), reason.ToString());
        }
        foreach (var reason in Enum.GetValues<WorkerCallableCoverageReason>().Where(static reason =>
            reason is not WorkerCallableCoverageReason.None and not WorkerCallableCoverageReason.Unspecified))
        {
            Assert.That(LauncherPresentation.IncompleteGuidance(reason), Is.Not.Empty, reason.ToString());
        }
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => LauncherPresentation.UnknownGuidance(WorkerClaimReason.None)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => LauncherPresentation.IncompleteGuidance(WorkerCallableCoverageReason.None)));
    }

    [Test]
    public void SemanticLimitsDoNotRecommendIncreasingTimeAndReplayDoesNotBecomeRefuted()
    {
        Assert.That(LauncherPresentation.UnknownGuidance(WorkerClaimReason.UnsupportedExpression), Does.Contain("increasing a timeout cannot add missing semantics"));
        Assert.That(LauncherPresentation.UnknownGuidance(WorkerClaimReason.BackendUnavailable), Does.Contain("sharpproof doctor"));
        Assert.That(LauncherPresentation.UnknownGuidance(WorkerClaimReason.CounterexampleReplayFailed), Does.Contain("only a validated executable witness establishes a violation"));
        Assert.That(LauncherPresentation.UnknownGuidance(WorkerClaimReason.MethodTimeout), Does.Contain("SharpProofVerifyMethodWallTimeMilliseconds"));
        Assert.That(LauncherPresentation.SourcePrefix(new() { Path = "Subject.cs", Line = 12, Column = 7 }), Is.EqualTo("Subject.cs(12,7)"));
        Assert.That(LauncherPresentation.SourcePrefix(new()), Is.EqualTo("SharpProof"));
    }
}
