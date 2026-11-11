using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class PerfBaselineTests
{
    [Test]
    public void MissingReferenceNeverFails()
    {
        using var temporary = new TempDirectory("SharpProof-perf-missing-");

        var (verdict, failed) = PerfBaseline.Compare(temporary.FullName, Report(100));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed, Is.False);
            Assert.That(verdict, Does.Contain("nothing to compare"));
        }
    }

    [TestCase(120, false, false, "within the limit.")]
    [TestCase(126, false, false, "regression (advisory).")]
    [TestCase(126, true, true, "regression.")]
    [TestCase(80, true, false, "within the limit.")]
    public void TotalSolverWorkIsComparedWithTheReference(long total, bool enforced, bool expectedFailure, string expectedEnding)
    {
        using var temporary = new TempDirectory("SharpProof-perf-compare-");
        var reference = Path.Combine(temporary.FullName, PerfBaseline.ReferencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(reference)!);
        File.WriteAllText(reference,
            "{ \"totalRlimit\": 100, \"maximumIncreasePercent\": 25, \"enforced\": " + (enforced ? "true" : "false") + " }");

        var (verdict, failed) = PerfBaseline.Compare(temporary.FullName, Report(total));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed, Is.EqualTo(expectedFailure));
            Assert.That(verdict, Does.EndWith(expectedEnding));
        }
    }

    [Test]
    public void SummaryListsTotalsPercentilesAndCostliestCallables()
    {
        var report = Report(0) with
        {
            Callables = 3,
            TotalRlimit = 111,
            TotalElapsedMilliseconds = 6,
            CallableCosts =
            [
                Cost("M:C.Small", 1, 1),
                Cost("M:C.Large", 100, 3),
                Cost("M:C.Medium", 10, 2)
            ]
        };

        var summary = PerfBaseline.Summarize(report, "verdict line");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary, Does.Contain("verdict line"));
            Assert.That(summary, Does.Contain("| Solver work (rlimit) | 3 | 111 | 10 | 100 | 100 |"));
            Assert.That(summary.IndexOf("M:C.Large", StringComparison.Ordinal),
                Is.LessThan(summary.IndexOf("M:C.Medium", StringComparison.Ordinal)));
        }
    }

    private static PerfBaselineReport Report(long totalRlimit)
    {
        return new() { TotalRlimit = totalRlimit, CallableCosts = [] };
    }

    private static WorkerCallableCost Cost(string id, long rlimit, long milliseconds)
    {
        return new() { CallableId = id, ConsumedRlimit = rlimit, ElapsedMilliseconds = milliseconds };
    }
}
