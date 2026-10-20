using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ResourceEventsPreserveNormalReturnProofsAndConcreteRefutations(bool correct)
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (correct ? "1" : "2") +
            "); _ = new System.ArgumentOutOfRangeException(\"parameter\"); return 1;",
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task OrdinarySourceLocksStillBlockProofsPastSynchronization()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); lock (new object()) { } return 1;",
            WorkerClaimOutcome.Unknown);
    }

    [TestCase("new System.ArgumentOutOfRangeException(\"Queue is empty.\")")]
    [TestCase("new System.InvalidOperationException()")]
    [TestCase("new System.Collections.Generic.KeyNotFoundException((string)null)")]
    [TestCase("new System.ArgumentException()")]
    [TestCase("new System.ArgumentNullException()")]
    [TestCase("new System.ArgumentNullException(\"parameter\")")]
    [TestCase("new System.ArgumentOutOfRangeException()")]
    [TestCase("new System.InvalidOperationException((string)null)")]
    [TestCase("new System.DivideByZeroException()")]
    [TestCase("new System.IO.IOException()")]
    [TestCase("new System.ObjectDisposedException(\"object\")")]
    [TestCase("new System.AggregateException()")]
    [TestCase("new System.AggregateException((string)null)")]
    public async Task ResourceConstructionRefutesPurityWithAnObservedFrameworkLock(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var preparation = Prepare("[EnforcePure] public static void Target() { _ = " + expression + "; }");
        var verified = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome, Is.TypeOf<RefutedOutcome>(), verified.Reason.ToString());
        Assert.That(verified.LockWitness, Is.Not.Null);
        Assert.That(verified.Reason, Is.EqualTo(WorkerClaimReason.None));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ResourceLockWitnessesRespectUnknownCallPrefixes(bool unknownFirst)
    {
        const string construction = "_ = new System.ArgumentOutOfRangeException(\"parameter\");";
        var body = unknownFirst ? "operation(); " + construction : construction + " operation();";
        var preparation = Prepare("[EnforcePure] public static void Target(System.Action operation) { " + body + " }");
        var verified = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        if (unknownFirst)
        {
            Assert.That(verified.Outcome, Is.Null);
            Assert.That(verified.LockWitness, Is.Null);
        }
        else
        {
            Assert.That(verified.Outcome, Is.TypeOf<RefutedOutcome>(), verified.Reason.ToString());
            Assert.That(verified.LockWitness, Is.Not.Null);
        }
    }

    [TestCase("System.InvalidOperationException")]
    [TestCase("System.Collections.Generic.KeyNotFoundException")]
    public async Task ConstructorMessagePreconditionsSelectOnlyTheCallbackFreePath(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "string message",
            "Contract.Requires(message != null); _ = new " + type + "(message); return 0;", true, false, true);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "string message",
            "_ = new " + type + "(message); return 0;", false, false, false);
    }

    [TestCase("string message = null; _ = new System.ArgumentNullException(message: message, paramName: message = \"parameter\"); return 0;", false)]
    [TestCase("string message = \"message\"; _ = new System.ArgumentNullException(message: message, paramName: message = null); return 0;", true)]
    public async Task ConstructorNamedArgumentOrderPreservesTheEvaluatedMessage(string body, bool callbackFree)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "", body, callbackFree, false, callbackFree);
    }

    [TestCase("new System.ArgumentNullException()")]
    [TestCase("new System.ArgumentNullException(\"parameter\")")]
    [TestCase("new System.ArgumentOutOfRangeException(\"parameter\")")]
    [TestCase("new System.InvalidOperationException()")]
    [TestCase("new System.InvalidOperationException((string)null)")]
    [TestCase("new System.Collections.Generic.KeyNotFoundException((string)null)")]
    [TestCase("new System.AggregateException()")]
    [TestCase("new System.AggregateException((string)null)")]
    [TestCase("new System.Diagnostics.UnreachableException((string)null)")]
    public async Task ResourceMessageConstructorsRetainAmbientCallbackEffects(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "",
            "_ = " + expression + "; return 0;", false, false, false);
    }

    [TestCase("new System.Exception((string)null)")]
    [TestCase("new System.InvalidOperationException(\"message\")")]
    [TestCase("new System.Collections.Generic.KeyNotFoundException(\"message\")")]
    [TestCase("new System.ArgumentException((string)null)")]
    [TestCase("new System.ArgumentException(\"message\", \"parameter\")")]
    [TestCase("new System.ArgumentNullException(\"parameter\", \"message\")")]
    [TestCase("new System.ArgumentOutOfRangeException(\"parameter\", \"message\")")]
    public async Task ExplicitMessageConstructorEffectsKeepTheirCallbackFreeControls(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "",
            "_ = " + expression + "; return 0;", true, false, true);
    }
}
