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
        var factory = new IrFactory();
        var unsupported = Preparation(factory, WorkerClaimReason.UnsupportedBody);
        var successful = Preparation(factory, WorkerClaimReason.None);

        Assert.That(
            SharpProofWorker.CountSolverTargets([unsupported, successful]),
            Is.EqualTo(1));
        Assert.That(
            SharpProofWorker.CountSolverTargets([unsupported]),
            Is.Zero);
    }

    private static CompilerCallablePreparation Preparation(
        IrFactory factory, WorkerClaimReason reason)
    {
        return new CompilerCallablePreparation(
            factory,
            new WorkerCallableManifestEntry { CallableId = Guid.NewGuid().ToString("N") },
            [], [], reason, null);
    }
}
