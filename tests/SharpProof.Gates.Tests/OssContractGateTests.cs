using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

// Contracts on real code: every reviewed companion contract in
// oss-contracts.json gets exactly its expected verdict.
[TestFixture]
[NonParallelizable]
internal sealed class OssContractGateTests
{
    [Test]
    public async Task EveryReviewedContractGetsItsExpectedVerdict()
    {
        var result = await OssContractGate.RunAsync(RepositoryLayout.FindRoot(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Mismatches.Select(static row => $"{row.MethodId}: expected {row.Expected}, got {row.Actual} ({row.Reason})"),
            Is.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Contracts, Is.EqualTo(15));
            Assert.That(result.Proven, Is.EqualTo(2));
            Assert.That(result.Refuted, Is.Zero);
            Assert.That(result.Passed, Is.True);
        }
    }

    [Test]
    public void EachContractNamesACompanionOrSharesOne()
    {
        var contracts = OssContractGate.Load(RepositoryLayout.FindRoot()).Contracts;
        Assert.That(contracts.All(static contract => (contract.Companion is null) != (contract.SharedWith is null)), Is.True);
        Assert.That(contracts.Select(static contract => contract.MethodId).Distinct().Count(), Is.EqualTo(contracts.Length));
    }
}
