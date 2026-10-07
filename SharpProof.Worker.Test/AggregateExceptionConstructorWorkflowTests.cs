using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AggregateExceptionConstructorWorkflowTests
{
    [TestCase("[AllowedExceptions(typeof(System.AggregateException))] public static void Target() { throw new System.AggregateException(\"outer\", (System.Exception)null); }", WorkerClaimOutcome.Unknown)]
    [TestCase("[DoesNotThrow] public static object Target(System.Exception inner) { return new System.AggregateException(\"outer\", inner); }", WorkerClaimOutcome.Unknown)]
    [TestCase("[AllowedExceptions(typeof(System.AggregateException))] public static void Target() { throw new System.AggregateException(); }", WorkerClaimOutcome.Proven)]
    [TestCase("[AllowedExceptions(typeof(System.AggregateException))] public static void Target() { throw new System.AggregateException(\"outer\", new System.Exception()); }", WorkerClaimOutcome.Proven)]
    [TestCase("[DoesNotThrow] public static object Target() { return new System.Exception(\"outer\", (System.Exception)null); }", WorkerClaimOutcome.Proven)]
    [TestCase("[AllowedExceptions(typeof(System.AggregateException))] public static void Target() { throw new System.ArgumentNullException(\"inner\"); }", WorkerClaimOutcome.Refuted)]
    public async Task WorkerPublishesOnlySupportedConstructorExceptionEvidence(string declaration, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " + declaration + " }");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        await TestContext.Out.WriteLineAsync($"constructor workflow: expected={expected}; actual={claim.Outcome}; reason={claim.Reason}");
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
