using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusWallTimePolicyTests
{
    private static readonly ImmutableArray<string> ExpectedFailure = ["Corpus wall time 300.0s exceeds the 300s budget."];

    [TestCase(299999, false)]
    [TestCase(300000, false)]
    [TestCase(300001, true)]
    public void FixedCorpusPolicyPreservesItsStrictFiveMinuteBoundary(int milliseconds, bool exceeds)
    {
        var failures = CorpusGate.ValidateWallTime(TimeSpan.FromMilliseconds(milliseconds));
        if (exceeds)
        {
            Assert.That(failures, Is.EqualTo(ExpectedFailure));
        }
        else
        {
            Assert.That(failures, Is.Empty);
        }
    }
}
