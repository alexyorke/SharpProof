using NUnit.Framework;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A body that widens an input before multiplying it computes the same
// product term as a postcondition that widens it, so the two share one
// multiplier instead of needing a bit-level equivalence (NodaTime's
// TickArithmetic.DaysAndTickOfDayToTicks). A widened days + 1 stays linear
// in the widened days, so the slow branch's product distributes too.
[TestFixture]
public sealed class WidenedProductTests
{
    [TestCase("days >= -10675199 && days <= 10675198", "", true, TestName = "WidenedProductMatchesTheContractsProduct")]
    [TestCase("days >= -10675199 && days <= 10675198", " + 1", false, TestName = "WrongWidenedProductIsRefuted")]
    [TestCase("days <= 10675198", "", true, TestName = "WidenedSumProductMatchesTheContractsProduct")]
    [TestCase("days <= 10675198", " + 1", false, TestName = "WrongWidenedSumProductIsRefuted")]
    public async Task DaysAndTickOfDayToTicks(string range, string offset, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(range);
        System.ArgumentNullException.ThrowIfNull(offset);
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class TickArithmetic {
                private const long TicksPerDay = 864000000000L;
                public static long DaysAndTickOfDayToTicks(int days, long tickOfDay) {
                    Contract.Requires(
            """ + range + """
            );
                    Contract.Requires(tickOfDay >= 0 && tickOfDay < 864000000000L);
                    Contract.Ensures(Contract.Result<long>() == days * 864000000000L + tickOfDay
            """ + offset + """
            );
                    return days >= (int) (long.MinValue / TicksPerDay)
                        ? days * TicksPerDay + tickOfDay
                        : (days + 1) * TicksPerDay + tickOfDay - TicksPerDay;
                }
            }
            """, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var postconditions = response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.Postcondition)
            .Select(static claim => claim.ClaimId).ToHashSet(StringComparer.Ordinal);
        var results = response.ClaimResults.Where(result => postconditions.Contains(result.ClaimId)).ToArray();
        Assert.That(results.Select(static result => result.Outcome),
            Is.EqualTo(new[] { proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted }),
            () => string.Join("; ", results.Select(static result => result.Outcome + " " + result.Reason + " " + result.Diagnostic)));
    }
}
