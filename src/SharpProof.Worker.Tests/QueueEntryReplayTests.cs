using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed class QueueEntryReplayTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task QueueIncomingCounterexampleHasAConcreteRuntimeRepresentation(bool remove)
    {
        var body = remove ? "Contract.Requires(data != null && data.Count == 1); Contract.Ensures(Contract.Result<int>() == 17); return data.Dequeue();"
            : "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() > 0); return data.Count;";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public static class C { public static int Target(System.Collections.Generic.Queue<int> data) { " + body + " } }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparations.Single())!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, TestContext.CurrentContext.CancellationToken), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var check = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(check.Reason, Is.EqualTo(WorkerClaimReason.None));
        var replay = plan!.ReplayEffects(check.EntryModel, TestContext.CurrentContext.CancellationToken);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), replay.Unsupported?.Detail);
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue, Is.Not.Null);
        if (remove)
        {
            var actual = new Queue<int>([checked((int)replay.ReturnValue!.Integer)]);
            Assert.That(actual.Dequeue(), Is.EqualTo(replay.ReturnValue.Integer));
            Assert.That(replay.ReturnValue.Integer, Is.Not.EqualTo(17));
            Assert.That(actual.Count, Is.Zero);
        }
        else
        {
            Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(new Queue<int>().Count));
            Assert.That(replay.ReturnValue.Integer, Is.Zero);
        }
    }
}
