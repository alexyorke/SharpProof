using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CallableVerificationPolicyRegressionTests
{
    [Test]
    public void FailedLoweringDoesNotRefuteEffectClaimWhenRequiresArePresent()
    {
        const string claimId = "spc1:effect";
        var target = new CompilerCallablePreparation(
            new IrFactory(),
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Subject.Verify()",
                ClaimIds = [claimId],
                Assumptions = [
                    new WorkerAssumptionEvidence
                    {
                        Id = "spa1:requires",
                        Kind = WorkerAssumptionKind.Precondition
                    }
                ]
            },
            [],
            [],
            WorkerClaimReason.UnsupportedBody,
            null)
        {
            EffectClaims = [
                new CompilerEffectClaimArtifact
                {
                    ClaimId = claimId,
                    ContractKind = WorkerEffectContractKind.ZeroAllocations,
                    Outcome = WorkerClaimOutcome.Refuted,
                    Reason = WorkerClaimReason.None,
                    Certainty = WorkerEffectEvidenceCertainty.DefiniteViolation
                }
            ]
        };

        var result = CallableVerificationPolicy.FailedLowering(
            target,
            CancellationToken.None);
        var claim = result.Claims.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                claim.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(
                claim.EffectCertainty,
                Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(claim.EffectWitness, Is.Null);
            Assert.That(
                result.Callable.Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(
                result.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.SemanticUnknown));
        }
    }
}
