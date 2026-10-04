using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A loop that stores only to fields of fixed objects forgets just those fields
// at its header, and invariants may relate them to the loop's counters and to
// their entry values.
[TestFixture]
public sealed class WorkerVcLoopFieldTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public sealed class Counter {
            private int count;
            private bool flag;
            public void CountTo(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(count == n);
                count = 0;
                for (var i = 0; i < n; i++) { count++; }
            }
            public void AddN(int n) {
                Contract.Requires(n >= 0 && n <= 1000);
                Contract.Requires(count >= 0 && count <= 1000);
                Contract.Ensures(count == Contract.Old(count) + n);
                for (var i = 0; i < n; i++) { count++; }
            }
            public void KeepFlag(int n) {
                Contract.Ensures(flag);
                flag = true;
                for (var i = 0; i < n; i++) { flag = true; }
            }
            public void UntouchedField(int n) {
                Contract.Requires(flag);
                Contract.Ensures(flag);
                for (var i = 0; i < n; i++) { count++; }
            }
            public void WrongCountTo(int n) {
                Contract.Requires(n >= 1);
                Contract.Ensures(count == n + 1);
                count = 0;
                for (var i = 0; i < n; i++) { count++; }
            }
            public static void ParameterLoop([NotNull] Counter other, int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(other.count == n);
                other.count = 0;
                for (var i = 0; i < n; i++) { other.count++; }
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

    [TestCase("CountTo")]
    [TestCase("AddN")]
    [TestCase("KeepFlag")]
    [TestCase("UntouchedField")]
    [TestCase("ParameterLoop")]
    public void LoopFieldPostconditionIsProven(string method)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public void FalseLoopFieldPostconditionIsNotProven()
    {
        Assert.That(Claim("WrongCountTo").Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static WorkerClaimResult Claim(string method)
    {
        var response = s_response!;
        var name = "M:Counter." + method;
        var callable = response.Manifest.Callables.Single(callable => callable.CallableId == name ||
            callable.CallableId.StartsWith(name + "(", StringComparison.Ordinal) ||
            callable.CallableId.StartsWith(name + "~", StringComparison.Ordinal));
        return response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
    }
}
