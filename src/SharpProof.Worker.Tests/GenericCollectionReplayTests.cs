using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Worker.Test;

public sealed class GenericCollectionReplayTests
{
    [Test]
    public async Task GenericLocalCollectionBranchHasAReplayableNormalWitness()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "public static int Target(int[] values) { Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 17); " +
            "return Read<int>(values); static int Read<T>(System.Collections.Generic.IList<T> list) => list.Count == 0 ? 0 : 17; } }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparations.Single())!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, TestContext.CurrentContext.CancellationToken), Is.True, failure.ToString());
        using var session = new CallableSolverSession(candidate.Factory, new IrSmtBackendOptions());
        var result = await session.CheckAsync(plan!.NormalCompletionQuery(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var replay = plan.ReplayEffects(result.Model!.Assignments, TestContext.CurrentContext.CancellationToken);
        var calls = candidate.Program.Blocks.SelectMany(block => (block ?? throw new ArgumentNullException(nameof(block))).Instructions).OfType<IrCallInstruction>()
            .Select(call => candidate.Factory.GetOperationInfo((call ?? throw new ArgumentNullException(nameof(call))).Operation).Description is { } description ? candidate.Factory.GetString(description) : "unnamed");
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), "approximation=" + replay.ConsumedApproximation + "; calls=" + string.Join(",", calls));
        Assert.That(replay.ConsumedApproximation, Is.False, "calls=" + string.Join(",", calls));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(17));
    }
}
