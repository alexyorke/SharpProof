using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualCompilerAllowanceValidityPrecedesContradictoryEntryPresentation(bool validAllowance)
    {
        var allowed = validAllowance ? "System.Exception" : "System.String";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                [AllowedExceptions(typeof({{allowed}}))]
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Requires(value < 0);
                    return value;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: CancellationToken.None), out var preparations, cancellationToken: CancellationToken.None);
        var preparation = preparations.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        var evidence = preparation.EffectClaims.Single();
        Assert.That(preparation.Total!.ValidEffectClaimIds.Contains(evidence.ClaimId), Is.EqualTo(validAllowance));
        if (!validAllowance)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(evidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
                Assert.That(evidence.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
                Assert.That(evidence.Certainty, Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
            }
        }
        var entry = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), CancellationToken.None);
        Assert.That(entry.IsContradictory, Is.True);
        Assert.That(entry.ProofCore, Is.Not.Empty);
        var projected = EffectClaimResultAssembler.Assemble(preparation, evidence, entry, CancellationToken.None);

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        var expectedOutcome = validAllowance ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown;
        var expectedReason = validAllowance ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedContract;
        var expectedVacuity = validAllowance ? WorkerVacuityKind.ContradictoryPreconditions : WorkerVacuityKind.None;
        var result = AssertClaimVerdict(response, expectedOutcome, expectedReason, expectedVacuity);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(projected.Outcome, Is.EqualTo(expectedOutcome));
            Assert.That(projected.Reason, Is.EqualTo(expectedReason));
            Assert.That(projected.Vacuity, Is.EqualTo(expectedVacuity));
            Assert.That(result.EffectCertainty, Is.EqualTo(validAllowance
                ? WorkerEffectEvidenceCertainty.VacuousEntry : WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(projected.EffectCertainty, Is.EqualTo(result.EffectCertainty));
            Assert.That(result.EffectWitness, Is.Null);
            if (validAllowance)
            { Assert.That(result.ProofCore, Is.Not.Empty); }
            else
            { Assert.That(result.ProofCore, Is.Empty); }
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
