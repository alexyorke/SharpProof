using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerEffectClaimSealGuardTests
{
    [Test]
    public void SealRejectsNullClaimWithItsParameterName()
    {
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => CompilerEffectClaimArtifactCodec.Seal(null!)));

        Assert.That(error!.ParamName, Is.EqualTo("value"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SealRejectsMissingConstraintDataBeforeReplacingDigest(
        bool missingConstraint)
    {
        var claim = CreateClaim();
        CompilerEffectClaimArtifactCodec.Seal(claim);
        var originalDigest = claim.EvidenceSha256;
        if (missingConstraint)
        {
            { claim = claim with { Constraint = null! }; }
        }
        else
        {
            { claim = claim with { Constraint = claim.Constraint with { AllowedExceptionTypes = default } }; }
        }

        var error = Assert.Throws<InvalidDataException>(
            (Action)(() => CompilerEffectClaimArtifactCodec.Seal(claim)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message,
                Is.EqualTo("Compiler effect-claim evidence is invalid."));
            Assert.That(claim.EvidenceSha256, Is.EqualTo(originalDigest));
        }
    }

    [Test]
    public void SealProducesStableDigestForValidExceptionEvidence()
    {
        var claim = CreateClaim();
        CompilerEffectClaimArtifactCodec.Seal(claim);
        var originalDigest = claim.EvidenceSha256;

        Assert.That(WorkerProtocolJson.IsSha256(originalDigest), Is.True);
        Assert.That((Action)(() => CompilerEffectClaimArtifactCodec.Validate(claim)),
            Throws.Nothing);

        CompilerEffectClaimArtifactCodec.Seal(claim);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.EvidenceSha256, Is.EqualTo(originalDigest));
            Assert.That(claim.Constraint.AllowedExceptionTypes, Has.Length.EqualTo(1));
            Assert.That(claim.Constraint.AllowedExceptionTypes[0],
                Is.EqualTo("T:System.InvalidOperationException"));
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(claim.Reason,
                Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        }
    }

    private static CompilerEffectClaimArtifact CreateClaim()
    {
        return new CompilerEffectClaimArtifact
        {
            ClaimId = "seal-guard-control",
            ContractKind = WorkerEffectContractKind.AllowedExceptions,
            Outcome = WorkerClaimOutcome.Unknown,
            Reason = WorkerClaimReason.CounterexampleNotReplayable,
            Certainty = WorkerEffectEvidenceCertainty.Unavailable,
            Constraint = new CompilerEffectConstraintArtifact
            {
                AllowedExceptionTypes = ["T:System.InvalidOperationException"]
            },
            Evidence = "seal-guard-control:exception-unavailable"
        };
    }
}
