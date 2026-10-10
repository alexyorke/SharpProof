using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A constant `while (true)` loop leaves its CFG exit behind the constant's
// false edge. With a finally region, the region lowerer must still give that
// impossible exit a typed return, or the whole manifest fails to decode.
[TestFixture]
[NonParallelizable]
public sealed class ConstantLoopFinallyExitTests
{
    [TestCase("[DoesNotThrow]", "", "while (true) { try { return x; } finally { x = 1; } }", WorkerClaimOutcome.Proven)]
    [TestCase("[DoesNotThrow]", "", "while (true) { try { return 10 / x; } finally { x = 1; } }", WorkerClaimOutcome.Refuted)]
    [TestCase("[DoesNotThrow]", "", "while (true) { try { if (x > 0) return x; x = 1; } finally { x++; } }", WorkerClaimOutcome.Proven)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == Contract.Old(x));",
        "while (true) { try { return x; } finally { x = 1; } }", WorkerClaimOutcome.Proven)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 1);",
        "while (true) { try { return x; } finally { x = 1; } }", WorkerClaimOutcome.Refuted)]
    [TestCase("[EnforcePure]", "", "while (true) { try { return x; } finally { x = 1; } }", WorkerClaimOutcome.Proven)]
    [TestCase("[EnforcePure]", "", "while (true) { try { return x; } finally { State = 1; } }", WorkerClaimOutcome.Refuted)]
    [TestCase("[EnforcePure]", "", "for (;;) { try { return x; } catch (System.InvalidOperationException) { State = 1; } finally { x = 1; } }",
        WorkerClaimOutcome.Proven)]
    // Controls: an inlined source callee already returned through its frame.
    [TestCase("[DoesNotThrow]", "", "return Callee(x);", WorkerClaimOutcome.Proven)]
    [TestCase("[DoesNotThrow]", "", "return Callee(0);", WorkerClaimOutcome.Proven)]
    [TestCase("[EnforcePure]", "", "return ImpureCallee(x);", WorkerClaimOutcome.Refuted)]
    // Controls: the same bodies without the constant loop.
    [TestCase("[DoesNotThrow]", "", "try { return x; } finally { x = 1; }", WorkerClaimOutcome.Proven)]
    [TestCase("[EnforcePure]", "", "try { return x; } finally { State = 1; }", WorkerClaimOutcome.Refuted)]
    public async Task ConstantLoopFinallyBodiesVerify(string attribute, string contract, string body, WorkerClaimOutcome expected)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "private static int Callee(int x) { while (true) { try { return x; } finally { x = 1; } } } " +
            "private static int ImpureCallee(int x) { while (true) { try { return x; } finally { State = 1; } } } " + attribute +
            " public static int Target(int x) { " + contract + " " + body + " } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
