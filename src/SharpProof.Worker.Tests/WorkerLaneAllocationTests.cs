using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed class WorkerLaneAllocationTests
{
    [Test]
    public void CompilerAbstentionsDoNotConsumeSolverLaneCapacity()
    {
        var unsupported = Preparation(WorkerClaimReason.UnsupportedBody);
        var successful = Preparation(WorkerClaimReason.None);

        Assert.That(
            SharpProofWorker.CountSolverTargets([unsupported, successful]),
            Is.EqualTo(1));
        Assert.That(
            SharpProofWorker.CountSolverTargets([unsupported]),
            Is.Zero);
    }

    private static CompilerCallablePreparation Preparation(WorkerClaimReason reason)
    {
        return new CompilerCallablePreparation(
            new WorkerCallableManifestEntry { CallableId = Guid.NewGuid().ToString("N") }, reason);
    }
}
