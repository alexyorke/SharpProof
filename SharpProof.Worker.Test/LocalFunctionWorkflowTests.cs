using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A local function that captures nothing is the same source call as a
// private static method: the native verifier decides a caller of either
// alike, and a false postcondition never yields a proof.
[TestFixture]
public sealed class LocalFunctionWorkflowTests
{
    [TestCase("return Add(0, 1); static int Add(int a, int b) => a + b;", "return Add(0, 1);", 1, WorkerClaimOutcome.Proven)]
    [TestCase("int Add(int a, int b) { return a + b; } return Add(0, 1);", "return Add(0, 1);", 1, WorkerClaimOutcome.Proven)]
    [TestCase("return Twice(Add(0, 1)) - 1; static int Twice(int a) => Add(a, a); static int Add(int a, int b) => a + b;",
        "return Add(Add(0, 1), Add(0, 1)) - 1;", 1, WorkerClaimOutcome.Proven)]
    [TestCase("return Add(0, 1); static int Add(int a, int b) => a + b;", "return Add(0, 1);", 2, WorkerClaimOutcome.Refuted)]
    [TestCase("int Add(int a, int b) { return a + b; } return Add(0, 1);", "return Add(0, 1);", 2, WorkerClaimOutcome.Refuted)]
    [TestCase("return Twice(Add(0, 1)) - 1; static int Twice(int a) => Add(a, a); static int Add(int a, int b) => a + b;",
        "return Add(Add(0, 1), Add(0, 1)) - 1;", 2, WorkerClaimOutcome.Refuted)]
    public async Task LocalFunctionMatchesStaticMethod(string local, string method, int result, WorkerClaimOutcome outcome)
    {
        var actual = await Verify(local, result);
        var expected = await Verify(method, result);
        Assert.That(expected.Outcome, Is.EqualTo(outcome), expected.Reason);
        Assert.That(actual, Is.EqualTo(expected));
    }

    // Recursion, exceptions and static state behave as for the same static
    // method, whatever the verdict.
    [TestCase("return Fall(1); static int Fall(int n) => n <= 0 ? 0 : Fall(n - 1) + 1;", "return Fall(1);", 1)]
    [TestCase("return Fall(1); static int Fall(int n) => n <= 0 ? 0 : Fall(n - 1) + 1;", "return Fall(1);", 2)]
    [TestCase("return Check(input - 4); static int Check(int a) { if (a < 0) { throw new System.ArgumentException(); } return a; }",
        "return Check(input - 4);", 1)]
    [TestCase("return Check(input - 6); static int Check(int a) { if (a < 0) { throw new System.ArgumentException(); } return a; }",
        "return Check(input - 6);", 1)]
    [TestCase("return Seed() + 1; static int Seed() => s_seed;", "return Seed() + 1;", 1)]
    [TestCase("return Need(input - 4); static int Need(int a) { Contract.Requires(a > 0); return a; }", "return Need(input - 4);", 1)]
    [TestCase("return Need(input - 5); static int Need(int a) { Contract.Requires(a > 0); return a; }", "return Need(input - 5);", 0)]
    public async Task LocalFunctionAgreesWithStaticMethod(string local, string method, int result)
    {
        Assert.That(await Verify(local, result), Is.EqualTo(await Verify(method, result)));
    }

    // A local function that reads its caller's state does not run from its
    // arguments alone; a false postcondition through it is never proven.
    [TestCase("int Shift(int a) => a + input; return Shift(0);")]
    [TestCase("var offset = 2; int Shift(int a) => a + offset; return Shift(0);")]
    public async Task CapturingLocalFunctionIsNotProvenFalse(string body)
    {
        var (outcome, reason) = await Verify(body, 1);
        Assert.That(outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), reason);
    }

    private static async Task<(WorkerClaimOutcome Outcome, string Reason)> Verify(string body, int result)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "private static int Add(int a, int b) => a + b; " +
            "private static int s_seed; private static int Seed() => s_seed; " +
            "private static int Fall(int n) => n <= 0 ? 0 : Fall(n - 1) + 1; " +
            "private static int Check(int a) { if (a < 0) { throw new System.ArgumentException(); } return a; } " +
            "private static int Need(int a) { Contract.Requires(a > 0); return a; } " +
            "public static int Target(int input) { Contract.Requires(input == 5); Contract.Ensures(Contract.Result<int>() == " + result + "); " +
            body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        return (claim.Outcome, claim.Reason.ToString());
    }
}
