using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A target-typed `new(arguments)` is the same construction as
// `new Box(arguments)`: the native verifier decides both alike, and a
// violated constructor precondition never yields a proof.
[TestFixture]
public sealed class TargetTypedNewWorkflowTests
{
    [TestCase("Box b = new(2); return b.Value;", "Box b = new Box(2); return b.Value;", 2)]
    [TestCase("return ((Box)new(2)).Value;", "return new Box(2).Value;", 2)]
    [TestCase("return Read(new(2));", "return Read(new Box(2));", 2)]
    [TestCase("Box b = new(-1); return b.Value;", "Box b = new Box(-1); return b.Value;", -1)]
    [TestCase("return ((Box)new(-1)).Value;", "return new Box(-1).Value;", -1)]
    [TestCase("return Read(new(-1));", "return Read(new Box(-1));", -1)]
    public async Task TargetTypedConstructionMatchesExplicitConstruction(string targetTyped, string explicitly, int result)
    {
        var actual = await Verify(targetTyped, result);
        var expected = await Verify(explicitly, result);
        Assert.That(actual, Is.EqualTo(expected));
        if (result < 0)
        { Assert.That(actual.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven)); }
    }

    private static async Task<(WorkerClaimOutcome Outcome, string Reason)> Verify(string body, int result)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public sealed class Box { public int Value; public Box(int value) { Contract.Requires(value > 0); Value = value; } } " +
            "private static int Read(Box box) => box.Value; " +
            "public static int Target() { Contract.Ensures(Contract.Result<int>() == " + result + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        return (claim.Outcome, claim.Reason.ToString());
    }
}
