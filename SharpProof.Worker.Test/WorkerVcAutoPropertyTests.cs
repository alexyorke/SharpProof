using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A nonvirtual auto-property's setter stores its backing field, a `??` on a
// reference is a null test, and a metadata constructor yields a fresh object.
[TestFixture]
public sealed class WorkerVcAutoPropertyTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public sealed class Holder {
            private int Count { get; set; }
            public Holder Next { get; set; }
            public void Bump() {
                Contract.Ensures(Count == Contract.Old(Count) + 1);
                Count++;
            }
            public void Link([NotNull] Holder other) {
                Contract.Ensures(Next == other);
                Next = other;
            }
            public static string OrEmpty(string text) {
                Contract.Ensures(Contract.Result<string>() != null);
                return text ?? "";
            }
            public static System.Text.StringBuilder Fresh() {
                Contract.Ensures(Contract.Result<System.Text.StringBuilder>() != null);
                return new System.Text.StringBuilder();
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

    [TestCase("Bump")]
    [TestCase("Link")]
    [TestCase("OrEmpty")]
    [TestCase("Fresh")]
    public void PostconditionIsProven(string method)
    {
        var response = s_response!;
        var name = "M:Holder." + method;
        var callable = response.Manifest.Callables.Single(callable => callable.CallableId == name ||
            callable.CallableId.StartsWith(name + "(", StringComparison.Ordinal) ||
            callable.CallableId.StartsWith(name + "~", StringComparison.Ordinal));
        var claim = response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }
}
