using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class ExceptionalScalarResourceEffectsTests
{
    [Test]
    public async Task ExcludingOverflowKeepsTheScalarResultAndEffectFreePath()
    {
        using var project = new ShadowTestProject("""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                [EffectContract(SharpProofEffect.None)]
                public static int Target(int value) {
                    Contract.Requires(value != int.MinValue);
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return Math.Abs(value);
                }
            }
            """);
        var preparation = project.Snapshot.Callables.Single();
        var effects = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation,
            project.Request.Budgets, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(effects.Outcome, Is.TypeOf<ProvenOutcome>(), effects.Reason.ToString());
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Is.Not.Empty);
        Assert.That(response.ClaimResults.All(result => result is null
            ? throw new ArgumentNullException(nameof(result)) : result.Outcome == WorkerClaimOutcome.Proven), Is.True);
    }

    [Test]
    public async Task AnExceptionalResourceCallbackCanReachAnUnrelatedCatch()
    {
        using var project = new ShadowTestProject("""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value == int.MinValue);
                    Contract.Ensures(Contract.Result<int>() == 7);
                    try { return Math.Abs(value); }
                    catch (FormatException) { return -1; }
                    catch (OverflowException) { return 7; }
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExceptionalFrameworkEffectsCannotBeBoundedByReadsAllocationAndThrows(bool nullable)
    {
        var body = nullable ? "return value.Value;" : "Contract.Requires(value == int.MinValue); return System.Math.Abs(value);";
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                [EffectContract(SharpProofEffect.ReadsReceiverState | SharpProofEffect.ReadsArgumentState |
                    SharpProofEffect.ReadsCapturedState | SharpProofEffect.ReadsStaticState | SharpProofEffect.ReadsAmbientState |
                    SharpProofEffect.Allocates | SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(System.Exception) })]
                public static int Target(
            """ + (nullable ? "int?" : "int") + " value) { " + body + " } }");
        var preparation = project.Snapshot.Callables.Single();
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(candidate, Is.Not.Null);
        var calls = candidate.Program.Blocks.SelectMany(block => block is null
            ? throw new ArgumentNullException(nameof(block)) : block.Instructions).OfType<IrCallInstruction>().ToArray();
        Assert.That(calls, Has.Length.EqualTo(1));
        Assert.That(IrOpaqueCallSite.Effects(candidate.Factory, calls[0].Operation), Is.EqualTo(IrOpaqueCallEffects.All));
        var result = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation,
            project.Request.Budgets, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }
}
