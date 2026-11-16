using NUnit.Framework;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A body that widens an input before multiplying it computes the same
// product term as a postcondition that widens it, so the two share one
// multiplier instead of needing a bit-level equivalence (NodaTime's
// TickArithmetic.DaysAndTickOfDayToTicks).
[TestFixture]
public sealed class WidenedProductTests
{
    [TestCase("", true, TestName = "WidenedProductMatchesTheContractsProduct")]
    [TestCase(" + 1", false, TestName = "WrongWidenedProductIsRefuted")]
    public async Task DaysAndTickOfDayToTicks(string offset, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(offset);
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class TickArithmetic {
                private const long TicksPerDay = 864000000000L;
                public static long DaysAndTickOfDayToTicks(int days, long tickOfDay) {
                    Contract.Requires(days >= -10675199 && days <= 10675198);
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
        Assert.That(response.ClaimResults.Where(result => postconditions.Contains(result.ClaimId)).Select(static result => result.Outcome),
            Is.EqualTo(new[] { proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted }));
    }
}
