using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task ActualNativeEntryCoreRejectsAnIncompletePresentationMap()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Requires(value < 0);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: CancellationToken.None), out var preparations, cancellationToken: CancellationToken.None);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure, cancellationToken: CancellationToken.None), Is.True,
            failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var actual = await solver.VerifyEntryAsync(cancellationToken: CancellationToken.None);
        Assert.That(actual.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(actual.Reason, Is.EqualTo(WorkerClaimReason.None));
        var proof = (ProvenOutcome)actual.Outcome!;
        Assert.That(proof.Core, Is.Not.Empty);

        // These are presentation inputs only; the native proof and owned plan are unchanged.
        var labels = proof.Core.Distinct().Select((justification, ordinal) =>
                (Justification: justification, Label: "actual-core:" + ordinal))
            .ToDictionary(row => row.Justification, row => row.Label);
        var complete = CallableProofCore.Create(proof, labels);
        Assert.That(complete, Is.EqualTo(labels.Values.OrderBy(label => label, StringComparer.Ordinal)));
        labels.Remove(proof.Core[0]);
        Assert.That(CallableProofCore.Create(proof, labels), Is.Empty);
        Assert.That(plan!.CoreLabels(proof), Is.Not.Empty);

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Proven,
            expectedVacuity: WorkerVacuityKind.ContradictoryPreconditions);
        Assert.That(result.ProofCore, Is.Not.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }
}
