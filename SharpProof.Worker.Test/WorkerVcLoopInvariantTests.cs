using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Postconditions after loops that need an invariant. Candidate invariants are
// guesses; only ones the kernel proves inductive may be assumed.
[TestFixture]
public sealed class WorkerVcLoopInvariantTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static int CountUp(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() == n);
                var i = 0;
                while (i < n) { i++; }
                return i;
            }
            public static int CountDown(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() == 0);
                var i = n;
                while (i > 0) { i--; }
                return i;
            }
            public static int Nested(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() == n);
                var i = 0;
                while (i < n) {
                    var j = 0;
                    while (j < 3) { j++; }
                    i++;
                }
                return i;
            }
            public static int StepTwo(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() <= n);
                var i = 0;
                while (i < n) { i += 2; }
                return i;
            }
            [DoesNotThrow]
            public static int Sum([NotNull] int[] values) {
                var total = 0;
                for (var i = 0; i < values.Length; i++) { total += values[i]; }
                return total;
            }
            [DoesNotThrow]
            public static int SumFromOne([NotNull] int[] values) {
                var total = 0;
                for (var i = 1; i <= values.Length; i++) { total += values[i]; }
                return total;
            }
            private static int s_counter;
            [ZeroAllocations]
            public static int GuardedAllocation(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) {
                    if (i < 0) { total += new int[1].Length; }
                    total++;
                }
                return total;
            }
            [EnforcePure]
            public static int GuardedWrite(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) {
                    if (i < 0) { s_counter = i; }
                    total++;
                }
                return total;
            }
            [ZeroAllocations]
            public static int ReachedAllocation(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) {
                    if (i > 2) { total += new int[1].Length; }
                    total++;
                }
                return total;
            }
            public static int DoubleCount(int n) {
                Contract.Requires(n >= 0 && n <= 1000);
                Contract.Ensures(Contract.Result<int>() == 2 * n);
                var i = 0;
                var s = 0;
                while (i < n) { i++; s += 2; }
                return s;
            }
            public static int WrongDoubleCount(int n) {
                Contract.Requires(n >= 1 && n <= 1000);
                Contract.Ensures(Contract.Result<int>() == 2 * n + 1);
                var i = 0;
                var s = 0;
                while (i < n) { i++; s += 2; }
                return s;
            }
            public static int NeedsNoNegative(int n) {
                Contract.Ensures(Contract.Result<int>() == n);
                var i = 0;
                while (i < n) { i++; }
                return i;
            }
        }
        """;

    private static WorkerVerifyResponse? s_response;

    [OneTimeSetUp]
    public async Task Verify()
    {
        using var project = WorkerTests.TestProject.Create(Source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        s_response = await worker.VerifyAsync(request);
        Assert.That(s_response.Errors, Is.Empty);
    }

    [TestCase("CountUp")]
    [TestCase("CountDown")]
    [TestCase("Nested")]
    public void InductiveInvariantProvesThePostcondition(string method)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // s == 2 * i is beyond the templates; Spacer proposes it and the kernel
    // proves it inductive.
    [Test]
    public void SpacerProposalProvesThePostcondition()
    {
        var claim = Claim("DoubleCount");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // No invariant proves a false postcondition.
    [Test]
    public void FalseLoopPostconditionIsNotProven()
    {
        Assert.That(Claim("WrongDoubleCount").Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // i <= n is not inductive when i steps by two; n = 1 returns 2.
    [Test]
    public void NonInductiveGuessIsNotAssumed()
    {
        Assert.That(Claim("StepTwo").Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    // i >= 0 keeps every read in bounds.
    [Test]
    public void InductiveInvariantProvesAnEffectClaim()
    {
        var claim = Claim("Sum");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // The last iteration reads values[values.Length].
    [Test]
    public void OutOfBoundsLoopReadIsRefuted()
    {
        Assert.That(Claim("SumFromOne").Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    // i >= 0 rules out the guarded allocation and static write.
    [TestCase("GuardedAllocation")]
    [TestCase("GuardedWrite")]
    public void InductiveInvariantProvesEffectSiteClaims(string method)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // The fourth iteration allocates.
    [Test]
    public void ReachableLoopAllocationIsRefuted()
    {
        Assert.That(Claim("ReachedAllocation").Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    // Without n >= 0 a negative n returns 0.
    [Test]
    public void FalsePostconditionAfterALoopIsRefuted()
    {
        Assert.That(Claim("NeedsNoNegative").Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    // The Requires clause that justifies an invariant is reported as used.
    [Test]
    public void InvariantPremisesAreUsedAssumptions()
    {
        var response = s_response!;
        var callable = response.Manifest.Callables.Single(callable =>
            callable.CallableId.Contains(".CountUp(", StringComparison.Ordinal));
        var claim = response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
        Assert.That(claim.Assumptions.Single().Used, Is.True);
    }

    private static WorkerClaimResult Claim(string method)
    {
        var response = s_response!;
        var callable = response.Manifest.Callables.Single(callable =>
            callable.CallableId.Contains("." + method + "(", StringComparison.Ordinal));
        return response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
    }
}
