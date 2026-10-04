using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A nonvirtual auto-property's setter stores its backing field, a `??` on a
// reference is a null test (also when it assigns its own operand), a metadata
// constructor yields a fresh object and an interface setter is an opaque call,
// as is a source callee whose body does not lower or a constructor that is not
// plain.
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
        public sealed class Keyed {
            private int _key = 3;
            public Keyed(int key) {
                Contract.Requires(key >= 0);
                _key = key;
            }
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
            private static int Scale(int n) {
                Contract.Requires(n >= 0);
                var scaled = 1.5 * n;
                return (int)scaled;
            }
            public static int KeepAfterScale(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() == n);
                Scale(n);
                return n;
            }
            public static Keyed MakeKeyed(int key) {
                Contract.Requires(key >= 0);
                Contract.Ensures(Contract.Result<Keyed>() != null);
                return new Keyed(key);
            }
            public static System.Text.StringBuilder Fresh() {
                Contract.Ensures(Contract.Result<System.Text.StringBuilder>() != null);
                return new System.Text.StringBuilder();
            }
            public static int Mask(int x) {
                Contract.Ensures(Contract.Result<int>() >= 0);
                return checked(x & int.MaxValue);
            }
            public void MaskCount() {
                Contract.Ensures(Count == (Contract.Old(Count) & int.MaxValue));
                Count &= int.MaxValue;
            }
            public static int MaskRefuted(int x) {
                Contract.Requires(x == -1);
                Contract.Ensures(Contract.Result<int>() == 0);
                return int.MaxValue & x;
            }
            public static int MaskUnknown([NotNull] System.Version version) {
                Contract.Ensures(Contract.Result<int>() == 1);
                return version.GetHashCode() & int.MaxValue;
            }
            public static int MaskElement(int x) {
                Contract.Ensures(Contract.Result<int>() == ((Contract.Old(x) & 5) * 100 + 1));
                int[] values = new int[1];
                values[0] = x;
                int i = 0;
                values[i++] &= (values[0] = 5);
                return values[0] * 100 + i;
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
    [TestCase("KeepAfterScale")]
    [TestCase("MakeKeyed")]
    [TestCase("Mask")]
    [TestCase("MaskElement")]
    [TestCase("MaskCount")]
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

    [Test]
    public void IntegerMaskCounterexampleReplays()
    {
        Assert.That(Claim("MaskRefuted").Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public void IntegerMaskDoesNotRefuteAnApproximateHash()
    {
        Assert.That(Claim("MaskUnknown").Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [Test]
    public async Task RepeatedSourceCallsProveWithinSharedConstructionLimit()
    {
        var source = new System.Text.StringBuilder("using SharpProof.Attributes; public static class Subject { public static int Target(int x) { Contract.Requires(x == 0); Contract.Ensures(Contract.Result<int>() == 32); var value = x; ");
        for (var index = 0; index < 32; index++)
        { source.Append("value = Increment(value); "); }
        source.Append("return value; } private static int Increment(int value) => unchecked(value + 1);");
        for (var index = 0; index < 64; index++)
        {
            source.Append(" private static int Padding");
            source.Append(index);
            source.Append("() => 0;");
        }
        source.Append('}');
        using var project = new ShadowTestProject(source.ToString());
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // A recursive call, one whose callee body does not lower, or one to a
    // constructor with field initializers is an unknown call after its
    // callee's preconditions, which stay checked.
    [TestCase(".Depth(")]
    [TestCase(".KeepAfterScale(")]
    [TestCase(".MakeKeyed(")]
    public void UnknownCallKeepsItsPrecondition(string method)
    {
        using var project = new ShadowTestProject(Source);
        var caller = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains(method, StringComparison.Ordinal));
        Assert.That(caller.Total, Is.Not.Null);
        Assert.That(caller.Total!.CallPreconditions, Is.Not.Empty);
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
