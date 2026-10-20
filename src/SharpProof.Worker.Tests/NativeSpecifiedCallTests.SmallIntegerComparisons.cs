using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    private static IEnumerable<TestCaseData> SmallIntegerComparisonCases()
    {
        foreach (var element in ImmutableArray.Create("sbyte", "byte", "short", "ushort", "char"))
        {
            foreach (var correct in ImmutableArray.Create(false, true))
            { yield return new TestCaseData(element, correct); }
        }
    }

    [TestCaseSource(nameof(SmallIntegerComparisonCases))]
    public async Task SmallIntegerCompareToPreservesTheExactDifference(string element, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract(element + " left, " + element + " right", "Contract.Ensures(Contract.Result<int>() == " +
            (correct ? "(int)left - (int)right" : "(left < right ? -1 : left > right ? 1 : 0)") + "); return left.CompareTo(right);",
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
    }

    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    public async Task SmallIntegerCompareToRetainsKnownEffects(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", element + " left, " + element + " right",
            "return left.CompareTo(right);", true, true, true);
    }
}
