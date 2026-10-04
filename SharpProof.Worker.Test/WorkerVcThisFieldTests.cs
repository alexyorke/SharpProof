using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Scalar fields of `this` in a class instance member are modeled like those
// of any other object: `this` is a never-null trailing input, a value, and
// the receiver of inlined instance callees.
[TestFixture]
public sealed class WorkerVcThisFieldTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public sealed class Counter {
            private int count;
            private bool flag;
            public void Increment() {
                Contract.Ensures(count == Contract.Old(count) + 1);
                count++;
            }
            public void CompoundAdd(int amount) {
                Contract.Requires(amount >= 0 && amount < 100);
                Contract.Requires(count >= 0 && count < 1000);
                Contract.Ensures(count == Contract.Old(count) + amount);
                count += amount;
            }
            public void CompoundNoRequires(int amount) {
                Contract.Ensures(count == Contract.Old(count) + amount);
                count += amount;
            }
            public static void CompoundOther([NotNull] Counter other, int amount) {
                Contract.Ensures(other.count == Contract.Old(other.count) + amount);
                other.count += amount;
            }
            public void AddTwo() {
                Contract.Ensures(count == Contract.Old(count) + 2);
                count += 2;
            }
            public int SetThenRead() {
                Contract.Ensures(Contract.Result<int>() == 7);
                count = 7;
                return count;
            }
            public int ReadsEntry() {
                Contract.Requires(count > 0);
                Contract.Ensures(Contract.Result<int>() > 0);
                return count;
            }
            public int EntryValueIsUnknown() {
                Contract.Ensures(Contract.Result<int>() == 0);
                return count;
            }
            public int OtherField() {
                Contract.Requires(count == 3);
                Contract.Ensures(Contract.Result<int>() == 3);
                flag = true;
                return count;
            }
            public int MayAlias([NotNull] Counter other) {
                Contract.Ensures(Contract.Result<int>() == 1);
                count = 1;
                other.count = 2;
                return count;
            }
            public void WrongIncrement() {
                Contract.Ensures(count == Contract.Old(count) + 2);
                count++;
            }
            private void Bump() { count++; }
            public void IncrementViaHelper() {
                Contract.Ensures(count == Contract.Old(count) + 1);
                Bump();
            }
            public static void IncrementOther([NotNull] Counter other) {
                Contract.Ensures(other.count == Contract.Old(other.count) + 1);
                other.Bump();
            }
            public int Distinct([NotNull] Counter other) {
                Contract.Requires(other != this);
                Contract.Ensures(Contract.Result<int>() == 1);
                count = 1;
                other.count = 2;
                return count;
            }
            public bool SameAs(Counter other) {
                Contract.Ensures(Contract.Result<bool>() == (other == this));
                return other == this;
            }
            public void ExplicitThis() {
                Contract.Ensures(this.count == 4);
                this.count = 4;
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

    [TestCase("Increment", WorkerClaimOutcome.Proven)]
    [TestCase("CompoundAdd", WorkerClaimOutcome.Proven)]
    [TestCase("CompoundNoRequires", WorkerClaimOutcome.Proven)]
    [TestCase("CompoundOther", WorkerClaimOutcome.Proven)]
    [TestCase("AddTwo", WorkerClaimOutcome.Proven)]
    [TestCase("SetThenRead", WorkerClaimOutcome.Proven)]
    [TestCase("ReadsEntry", WorkerClaimOutcome.Proven)]
    [TestCase("EntryValueIsUnknown", WorkerClaimOutcome.Refuted)]
    [TestCase("OtherField", WorkerClaimOutcome.Proven)]
    [TestCase("MayAlias", WorkerClaimOutcome.Refuted)]
    [TestCase("WrongIncrement", WorkerClaimOutcome.Refuted)]
    [TestCase("ExplicitThis", WorkerClaimOutcome.Proven)]
    [TestCase("IncrementViaHelper", WorkerClaimOutcome.Proven)]
    [TestCase("IncrementOther", WorkerClaimOutcome.Proven)]
    [TestCase("Distinct", WorkerClaimOutcome.Proven)]
    [TestCase("SameAs", WorkerClaimOutcome.Proven)]
    public void FieldsOfThisAreModeled(string method, WorkerClaimOutcome expected)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    [Test]
    public void CounterexampleNamesTheReceiver()
    {
        var claim = Claim("EntryValueIsUnknown");
        Assert.That(claim.Model.Select(value => value.Variable), Does.Contain("this"));
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
