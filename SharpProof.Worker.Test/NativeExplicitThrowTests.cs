using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `throw e` raises an exception of e's static type, or NullReferenceException
// when e is null. Handlers are chosen by that type; a handler for a subtype
// of it may or may not match, so the search takes both ways.
[TestFixture]
public sealed class NativeExplicitThrowTests
{
    [TestCase("throw new System.InvalidOperationException();")]
    [TestCase("if (flag) throw new System.InvalidOperationException(\"failed\");")]
    [TestCase("throw new System.ArgumentException(\"bad\", nameof(flag));")]
    public async Task ReachableThrowsRefuteDoesNotThrow(string body)
    {
        var result = await ExceptionsAsync("[DoesNotThrow] public static void Target(bool flag) { " + body + " }");
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.ExceptionWitness!.Kind, Is.EqualTo(IrExceptionKind.Explicit));
    }

    [TestCase("catch (System.InvalidOperationException) { }", typeof(ProvenOutcome))]
    [TestCase("catch (System.Exception) { }", typeof(ProvenOutcome))]
    [TestCase("catch { }", typeof(ProvenOutcome))]
    [TestCase("catch (System.ArgumentException) { }", typeof(RefutedOutcome))]
    public async Task HandlersMatchTheThrownType(string handler, Type outcome)
    {
        var result = await ExceptionsAsync("[DoesNotThrow] public static void Target(bool flag) { " +
            "try { throw new System.InvalidOperationException(); } " + handler + " }");
        Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString());
    }

    [TestCase("catch (System.ObjectDisposedException) { }", null)]
    [TestCase("catch (System.ObjectDisposedException) { } catch (System.InvalidOperationException) { }", typeof(ProvenOutcome))]
    public async Task HandlersForSubtypesMayMatch(string handlers, Type? outcome)
    {
        var result = await ExceptionsAsync("[DoesNotThrow] public static void Target(System.InvalidOperationException error) { " +
            "if (error == null) { return; } try { throw error; } " + handlers + " }");
        if (outcome != null)
        { Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString()); }
        else
        {
            Assert.That(result.Outcome, Is.Not.TypeOf<RefutedOutcome>());
            Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        }
    }

    [Test]
    public async Task ThrowingNullThrowsNullReference()
    {
        var result = await ExceptionsAsync("[DoesNotThrow] public static void Target(System.InvalidOperationException error) { " +
            "try { throw error; } catch (System.InvalidOperationException) { } }");
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.ExceptionWitness!.Kind, Is.EqualTo(IrExceptionKind.NullReference));
    }

    // A site is allowed when its static type derives from an allowed type; a
    // created exception of another type refutes; a thrown variable's runtime
    // type may be an allowed subtype, so it stays unknown.
    [TestCase("throw new System.ObjectDisposedException(\"gone\");", typeof(ProvenOutcome))]
    [TestCase("throw new System.ArgumentException();", typeof(RefutedOutcome))]
    [TestCase("throw (System.Exception)new System.InvalidOperationException();", null)]
    public async Task AllowedExceptionsCompareThrownTypes(string body, Type? outcome)
    {
        var result = await ExceptionsAsync("[AllowedExceptions(typeof(System.InvalidOperationException))] " +
            "public static void Target(bool flag) { if (flag) " + body + " }");
        if (outcome != null)
        { Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString()); }
        else
        {
            Assert.That(result.Outcome, Is.Not.TypeOf<RefutedOutcome>());
            Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        }
    }

    [Test]
    public async Task ThrownExceptionsAllocate()
    {
        var preparation = Prepare("[ZeroAllocations] public static void Target(bool flag) { throw new System.InvalidOperationException(); }");
        var result = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
    }

    private static async Task<PassiveCallableCheckResult> ExceptionsAsync(string method)
    { return await NativeExceptionEffectVerifier.VerifyAsync(Prepare(method), new WorkerBudgets()); }

    private static CompilerCallablePreparation Prepare(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.EffectClaims.Length != 0);
    }
}
