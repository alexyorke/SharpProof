using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ContractMatrixMathAbsRegressionTests
{
    private static IEnumerable<int> AbsSeeds =>
        Enumerable.Range(56, 8).Concat(Enumerable.Range(120, 8));

    [TestCaseSource(nameof(AbsSeeds))]
    public async Task SupplementalAbsCaseAgreesWithClrAndWorker(int seed)
    {
        var result = await ContractMatrixLongFuzzTests.ProbeSpecAbsSeed(seed);
        var offset = seed % 64 - 56;
        var falseClaim = (offset & 2) != 0;
        var broadCatch = (offset & 1) != 0;
        var secondAnchor = (offset & 4) != 0;
        var oddCycle = seed / 64 % 2 != 0;
        var anchor = oddCycle ? (secondAnchor ? 1 : 0) : (secondAnchor ? int.MinValue : -1);
        var expectedReturn = anchor == int.MinValue ? (broadCatch ? 9 : 7) : Math.Abs(anchor);
        var expectedState = anchor == int.MinValue ? 2 : 1;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Return, Is.EqualTo(expectedReturn));
            Assert.That(result.State, Is.EqualTo(expectedState));
            Assert.That(result.FalseClaim, Is.EqualTo(falseClaim));
            Assert.That(result.Outcome, Is.EqualTo(falseClaim
                ? WorkerClaimOutcome.Refuted
                : WorkerClaimOutcome.Proven));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
        }
    }
}
