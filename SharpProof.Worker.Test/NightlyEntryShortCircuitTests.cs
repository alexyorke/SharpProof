using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NightlyEntryShortCircuitTests
{
    [TestCase("State = x;", false)]
    [TestCase("State = x;", true)]
    [TestCase("State--;", false)]
    [TestCase("State--;", true)]
    [TestCase("int w = 3; while (w > 0) { w--; State--; }", false)]
    [TestCase("int w = 3; while (w > 0) { w--; State--; }", true)]
    public async Task StaticWriteIsNotProvenPure(string body, bool hasRequires)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var requires = hasRequires ? "Contract.Requires(true || x == 0); " : "";
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[EnforcePure] public static int Target(int x) { " + requires + body + " return 0; } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var callable = artifact.Callables.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId.Contains("Target", StringComparison.Ordinal)));
        var total = callable.Total!;
        var writes = total.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).Count(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Write));
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            "body=" + body + " requires=" + hasRequires + " reason=" + claim.Reason +
            " claimId=" + claim.ClaimId + " valid=" + total.ValidEffectClaimIds.Contains(claim.ClaimId) +
            " effectsComplete=" + total.EffectsCompleteAtEntry + " writes=" + writes);
    }
    [TestCase(null)]
    [TestCase("true || x == 0")]
    [TestCase("x == 0 || x == 1")]
    [TestCase("x == 0 && x >= 0")]
    [TestCase("!(true) ? x > 1 : x >= 0")]
    public async Task ReachableThrowIsRefutedWithShortCircuitRequires(string? condition)
    {
        var requires = condition == null ? "" : "Contract.Requires(" + condition + "); ";
        var source = "using SharpProof.Attributes; public static class C { " +
            "[DoesNotThrow] public static void Target(int x) { " + requires +
            "if (true) throw new System.IO.IOException(); } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            "condition=" + condition + " reason=" + response.ClaimResults.Single().Reason);
    }

    [TestCase(null)]
    [TestCase("true")]
    [TestCase("true || values != null")]
    [TestCase("!(true) ? values != null && values.Length > 1 : values != null")]
    public async Task ThrowAllocationIsRefutedWithShortCircuitRequires(string? condition)
    {
        var requires = condition == null ? "" : "Contract.Requires(" + condition + "); ";
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ZeroAllocations] public static void Target(int[]? values) { " + requires +
            "throw new System.ArgumentException(\"m\"); } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            "condition=" + condition + " reason=" + response.ClaimResults.Single().Reason);
    }
    [TestCase("try { throw new System.IO.IOException(); } finally { State++; }")]
    [TestCase("try { throw new System.IO.IOException(); } catch (System.IO.IOException) when (x < 0) { }")]
    public async Task RegionThrowRemainsReachableAfterShortCircuitRequires(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[DoesNotThrow] public static void Target(int x) { Contract.Requires(true || x == 0); " +
            body + " } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            "body=" + body + " reason=" + response.ClaimResults.Single().Reason);
    }

    [Test]
    public async Task ImpossibleShortCircuitRequiresDoesNotCreateAThrowWitness()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[DoesNotThrow] public static void Target(int x) { Contract.Requires(false && x == 0); " +
            "throw new System.IO.IOException(); } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("true || x == 0")]
    [TestCase("false && x == 0")]
    [TestCase("x == 0 || x == 1")]
    [TestCase("x == 0 && x == 1")]
    public async Task CompilerElidedConditionalCallCannotHideFollowingThrow(string condition)
    {
        System.ArgumentNullException.ThrowIfNull(condition);
        var source = "using System.Diagnostics; using SharpProof.Attributes; public static class C { " +
            "[Conditional(\"TRACE\")] private static void Log(bool value) { } " +
            "[DoesNotThrow] public static void Target(int x) { Log(" + condition + "); " +
            "throw new System.IO.IOException(); } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            response.ClaimResults.Single().Reason.ToString());
    }

    [Test]
    public async Task CompilerElidedConditionalCallInTryFinallyKeepsThrowReachable()
    {
        const string source = "using System.Diagnostics; using SharpProof.Attributes; public static class C { " +
            "public static int State; [Conditional(\"TRACE\")] private static void Log(bool value) { } " +
            "[DoesNotThrow] public static void Target(int x) { try { Log(x == 0 || x == 1); " +
            "throw new System.IO.IOException(); } finally { State++; } } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            response.ClaimResults.Single().Reason.ToString());
    }
}
