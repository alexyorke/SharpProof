using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ContractMatrixClaimSelectionRegressionTests
{
    [Test]
    public async Task TargetPostconditionAgreesWithClrAdmission(
        [Range(0, 47)] int slot, [Range(0, 3)] int rewrite)
    {
        var result = await ContractMatrixLongFuzzTests.ProbeContractCase(slot, rewrite);
        if (result.Claim.Outcome == WorkerClaimOutcome.Unknown)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(rewrite, Is.Not.Zero, "Pinned source shapes must retain concrete verdicts.");
                Assert.That(result.Claim.Reason, Is.Not.EqualTo(WorkerClaimReason.None));
            }
            return;
        }
        Assert.That(result.Claim.Outcome, Is.EqualTo(result.HasWitness
            ? WorkerClaimOutcome.Refuted
            : WorkerClaimOutcome.Proven));
    }
}
