using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Element stores carry their values: later reads and postconditions see them,
// aliases are compared by identity, and Contract.Old reads entry contents.
[TestFixture]
public sealed class WorkerVcHeapTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static int StoreThenRead([NotNull] int[] values) {
                Contract.Requires(values.Length > 0);
                Contract.Ensures(Contract.Result<int>() == 7);
                values[0] = 7;
                return values[0];
            }
            public static void SetFirst([NotNull] int[] values) {
                Contract.Requires(values.Length > 0);
                Contract.Ensures(values[0] == 7);
                values[0] = 7;
            }
            public static void Increment([NotNull] int[] values) {
                Contract.Requires(values.Length > 0);
                Contract.Ensures(values[0] == Contract.Old(values[0]) + 1);
                values[0]++;
            }
            public static int MayAlias([NotNull] int[] left, [NotNull] int[] right) {
                Contract.Requires(left.Length > 0 && right.Length > 0);
                Contract.Ensures(Contract.Result<int>() == 1);
                left[0] = 1;
                right[0] = 2;
                return left[0];
            }
            public static int Distinct([NotNull] int[] left, [NotNull] int[] right) {
                Contract.Requires(left.Length > 0 && right.Length > 0);
                Contract.Requires(left != right);
                Contract.Ensures(Contract.Result<int>() == 1);
                left[0] = 1;
                right[0] = 2;
                return left[0];
            }
            private static int First(int first, int second) => first;
            public static int EvaluationOrder([NotNull] int[] values) {
                Contract.Requires(values.Length > 0);
                Contract.Ensures(Contract.Result<int>() == 0);
                values[0] = 0;
                return First(values[0], values[0] = 5);
            }
            public static int WrongValue([NotNull] int[] values) {
                Contract.Requires(values.Length > 0);
                Contract.Ensures(Contract.Result<int>() == 8);
                values[0] = 7;
                return values[0];
            }
            public static int OverwrittenInLoop([NotNull] int[] values) {
                Contract.Requires(values.Length > 0);
                Contract.Ensures(Contract.Result<int>() == 9);
                values[0] = 9;
                for (var i = 1; i < values.Length; i++) { values[0] = 1; }
                return values[0];
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
        Assert.That(s_response.Errors, Is.Empty, string.Join("; ", s_response.Errors.Select(error => error.Code + ": " + error.Message)));
    }

    [TestCase("StoreThenRead", WorkerClaimOutcome.Proven)]
    [TestCase("SetFirst", WorkerClaimOutcome.Proven)]
    [TestCase("Increment", WorkerClaimOutcome.Proven)]
    [TestCase("MayAlias", WorkerClaimOutcome.Refuted)]
    [TestCase("Distinct", WorkerClaimOutcome.Proven)]
    [TestCase("EvaluationOrder", WorkerClaimOutcome.Proven)]
    [TestCase("WrongValue", WorkerClaimOutcome.Refuted)]
    public void StoresAreModeled(string method, WorkerClaimOutcome expected)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    // A loop forgets array contents at its head, so the earlier store cannot
    // prove a value the loop may overwrite.
    [Test]
    public void LoopStoresAreNotIgnored()
    {
        Assert.That(Claim("OverwrittenInLoop").Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static WorkerClaimResult Claim(string method)
    {
        var response = s_response!;
        var callable = response.Manifest.Callables.Single(callable =>
            callable.CallableId.Contains("." + method + "(", StringComparison.Ordinal));
        return response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
    }
}
