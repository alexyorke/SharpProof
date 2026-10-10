using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An expression-bodied getter (`int P => 0;`) is the same source getter as
// `int P { get => 0; }`: the native verifier decides a caller of either
// alike, and a false postcondition never yields a proof.
[TestFixture]
public sealed class ExpressionBodiedGetterWorkflowTests
{
    [TestCase("return Arrow + 1;", "return Accessor + 1;", 1, WorkerClaimOutcome.Proven)]
    [TestCase("return new Grid().Arrow + 1;", "return new Grid().Accessor + 1;", 1, WorkerClaimOutcome.Proven)]
    [TestCase("return new Grid()[0, 1];", "return new Grid()[1];", 1, WorkerClaimOutcome.Proven)]
    [TestCase("return Arrow + 1;", "return Accessor + 1;", 2, WorkerClaimOutcome.Refuted)]
    [TestCase("return new Grid().Arrow + 1;", "return new Grid().Accessor + 1;", 2, WorkerClaimOutcome.Refuted)]
    [TestCase("return new Grid()[0, 1];", "return new Grid()[1];", 2, WorkerClaimOutcome.Refuted)]
    public async Task ExpressionBodiedGetterMatchesAccessorGetter(string arrow, string accessor, int result, WorkerClaimOutcome outcome)
    {
        var actual = await Verify(arrow, result);
        var expected = await Verify(accessor, result);
        Assert.That(expected.Outcome, Is.EqualTo(outcome), expected.Reason);
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static async Task<(WorkerClaimOutcome Outcome, string Reason)> Verify(string body, int result)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "private static int Arrow => 0; " +
            "private static int Accessor { get => 0; } " +
            "public sealed class Grid { public int Arrow => 0; public int Accessor { get => 0; } " +
            "public int this[int a, int b] => a + b; public int this[int a] { get => a; } } " +
            "public static int Target() { Contract.Ensures(Contract.Result<int>() == " + result + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        return (claim.Outcome, claim.Reason.ToString());
    }
}
