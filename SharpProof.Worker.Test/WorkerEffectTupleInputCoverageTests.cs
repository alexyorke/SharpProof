using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public async Task EffectTupleGuardRejectsRawInvalidInputWithoutConstructingAClaim(int invalidOutcome)
    {
        const string source = "using SharpProof.Attributes; public static class Subject { " +
            "[ZeroAllocations] public static int Target(int value) => value; }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var actual = preparations.Single().EffectClaims.Single();
        Assert.That(WorkerProtocolJson.HasValidEffectCertainty(actual.Outcome, actual.Reason, actual.Certainty), Is.True);
        Assert.DoesNotThrow(new Action(() =>
            EffectClaimResultAssembler.RequireSupportedEffectTuple(actual.Outcome, actual.Reason, actual.Certainty)));

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var original = WorkerProtocolJson.SerializeResponse(response);

        var exception = Assert.Throws<InvalidDataException>(new Action(() =>
            EffectClaimResultAssembler.RequireSupportedEffectTuple(
                (WorkerClaimOutcome)invalidOutcome, actual.Reason, actual.Certainty)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("Compiler effect-claim evidence has an unsupported result tuple."));
            Assert.That(WorkerProtocolJson.SerializeResponse(response), Is.EqualTo(original));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
