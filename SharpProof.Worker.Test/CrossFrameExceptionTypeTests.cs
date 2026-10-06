using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CrossFrameExceptionTypeTests
{
    [TestCase("catch (System.ArgumentException) { State = 1; }")]
    [TestCase("catch (System.ArgumentException) when ((State = 1) == 1) { State = 2; }")]
    public async Task ExactCalleeExceptionCannotExecuteSubtypeEffects(string handler)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[EnforcePure] public static void Target() { try { Helper(); } " + handler +
            " catch (System.Exception) { } } static void Helper() { throw new System.Exception(); } }";
        AssertRuntimeState(source, 0);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task ExactCalleeExceptionCannotExecuteThrowingSubtypeHandler()
    {
        const string source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[DoesNotThrow] public static void Target() { try { Helper(); } " +
            "catch (System.ArgumentException) { throw null; } catch (System.Exception) { } } " +
            "static void Helper() { throw new System.Exception(); } }";
        AssertRuntimeState(source, 0);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task ActualCalleeSubtypeCanExecuteHandlerEffects()
    {
        const string source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[EnforcePure] public static void Target() { try { Helper(); } " +
            "catch (System.ArgumentException) { State = 1; } catch (System.Exception) { } } " +
            "static void Helper() { throw new System.ArgumentException(); } }";
        AssertRuntimeState(source, 1);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task GenericCalleeExceptionCannotHideMatchingHandlerEffects()
    {
        const string source = "using SharpProof.Attributes; public class Error<T> : System.Exception { } " +
            "public static class C { public static int State; " +
            "[EnforcePure] public static void Target() { try { Helper<int>(); } " +
            "catch (Error<int>) { State = 1; } catch (System.Exception) { } } " +
            "static void Helper<T>() { throw new Error<T>(); } }";
        AssertRuntimeState(source, 1);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }

    private static void AssertRuntimeState(string source, int expected)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("CrossFrameTypeRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("CrossFrameTypeRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            Assert.DoesNotThrow(new Action(() => type.GetMethod("Target")!.Invoke(null, null)));
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(expected));
        }
        finally { context.Unload(); }
    }
}
