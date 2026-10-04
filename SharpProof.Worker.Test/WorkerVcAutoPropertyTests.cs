using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A nonvirtual auto-property's setter stores its backing field, a `??` on a
// reference is a null test (also when it assigns its own operand), a metadata
// constructor yields a fresh object and an interface setter is an opaque call.
[TestFixture]
public sealed class WorkerVcAutoPropertyTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public sealed class Bag : System.Collections.Generic.IEnumerable<int> {
            public System.Collections.Generic.IEnumerator<int> GetEnumerator() { yield return 1; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }
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
            public static string OrEmptyInPlace(string text) {
                Contract.Ensures(Contract.Result<string>() != null);
                text = text ?? "";
                return text;
            }
            public static int StoreThroughList([NotNull] System.Collections.Generic.IList<int> values) {
                Contract.Ensures(Contract.Result<int>() == 5);
                values[0] = 5;
                return 5;
            }
            public static int CountUpTo([NotNull] System.Collections.Generic.IEnumerable<int> values) {
                Contract.Ensures(Contract.Result<int>() >= 0);
                var count = 0;
                foreach (var value in values) { if (count < 1000) { count++; } }
                return count;
            }
            public static int ReadThroughRef(ref int value, [NotNull] ref System.Collections.Generic.List<int> list) {
                Contract.Ensures(Contract.Result<int>() == value);
                list.Add(value);
                return value;
            }
            public static int WriteThroughRef(ref int value) {
                Contract.Ensures(Contract.Result<int>() == 1);
                value = 1;
                return value;
            }
            public static int Depth(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() >= 0);
                if (n == 0) { return 0; }
                Depth(n - 1);
                return n;
            }
            public static int CountBag([NotNull] Bag bag) {
                Contract.Ensures(Contract.Result<int>() >= 0);
                var count = 0;
                foreach (var item in bag) { if (count < 1000) { count++; } }
                return count;
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
    [TestCase("OrEmptyInPlace")]
    [TestCase("StoreThroughList")]
    [TestCase("CountUpTo")]
    [TestCase("ReadThroughRef")]
    [TestCase("Depth")]
    [TestCase("CountBag")]
    public void PostconditionIsProven(string method)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // A ref parameter the body writes stores into its caller's variable.
    [Test]
    public void WrittenRefParameterStaysUnsupported()
    {
        Assert.That(Claim("WriteThroughRef").Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // A recursive call is an unknown call after its callee's preconditions,
    // which stay checked at the call.
    [Test]
    public void RecursiveCallKeepsItsPrecondition()
    {
        using var project = new ShadowTestProject(Source);
        var depth = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains(".Depth(", StringComparison.Ordinal));
        Assert.That(depth.Total, Is.Not.Null);
        Assert.That(depth.Total!.CallPreconditions, Is.Not.Empty);
    }

    private static WorkerClaimResult Claim(string method)
    {
        var response = s_response!;
        var name = "M:Holder." + method;
        var callable = response.Manifest.Callables.Single(callable => callable.CallableId == name ||
            callable.CallableId.StartsWith(name + "(", StringComparison.Ordinal) ||
            callable.CallableId.StartsWith(name + "~", StringComparison.Ordinal));
        return response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
    }
}
