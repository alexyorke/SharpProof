using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Scalar instance fields of explicit receivers are modeled: stores carry
// their values, objects are compared by identity, and Contract.Old reads the
// fields as the callable entered.
[TestFixture]
public sealed class WorkerVcFieldTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public sealed class Box {
            public int Value;
            public bool Flag;
        }
        public static class Subject {
            public static int StoreThenRead([NotNull] Box box) {
                Contract.Ensures(Contract.Result<int>() == 7);
                box.Value = 7;
                return box.Value;
            }
            public static void SetValue([NotNull] Box box) {
                Contract.Ensures(box.Value == 7);
                box.Value = 7;
            }
            public static void Increment([NotNull] Box box) {
                Contract.Ensures(box.Value == Contract.Old(box.Value) + 1);
                box.Value++;
            }
            public static int MayAlias([NotNull] Box left, [NotNull] Box right) {
                Contract.Ensures(Contract.Result<int>() == 1);
                left.Value = 1;
                right.Value = 2;
                return left.Value;
            }
            public static int Distinct([NotNull] Box left, [NotNull] Box right) {
                Contract.Requires(left != right);
                Contract.Ensures(Contract.Result<int>() == 1);
                left.Value = 1;
                right.Value = 2;
                return left.Value;
            }
            public static int ReadsEntry([NotNull] Box box) {
                Contract.Requires(box.Value > 0);
                Contract.Ensures(Contract.Result<int>() > 0);
                return box.Value;
            }
            public static int EntryValueIsUnknown([NotNull] Box box) {
                Contract.Ensures(Contract.Result<int>() == 0);
                return box.Value;
            }
            public static int OtherField([NotNull] Box box) {
                Contract.Requires(box.Value == 3);
                Contract.Ensures(Contract.Result<int>() == 3);
                box.Flag = true;
                return box.Value;
            }
            private static int First(int first, int second) => first;
            public static int EvaluationOrder([NotNull] Box box) {
                Contract.Ensures(Contract.Result<int>() == 0);
                box.Value = 0;
                return First(box.Value, box.Value = 5);
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
    [TestCase("SetValue", WorkerClaimOutcome.Proven)]
    [TestCase("Increment", WorkerClaimOutcome.Proven)]
    [TestCase("MayAlias", WorkerClaimOutcome.Refuted)]
    [TestCase("Distinct", WorkerClaimOutcome.Proven)]
    [TestCase("ReadsEntry", WorkerClaimOutcome.Proven)]
    [TestCase("EntryValueIsUnknown", WorkerClaimOutcome.Refuted)]
    [TestCase("OtherField", WorkerClaimOutcome.Proven)]
    [TestCase("EvaluationOrder", WorkerClaimOutcome.Proven)]
    public void FieldsAreModeled(string method, WorkerClaimOutcome expected)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    private static WorkerClaimResult Claim(string method)
    {
        var response = s_response!;
        var callable = response.Manifest.Callables.Single(callable =>
            callable.CallableId.Contains("." + method + "(", StringComparison.Ordinal));
        return response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
    }
}
