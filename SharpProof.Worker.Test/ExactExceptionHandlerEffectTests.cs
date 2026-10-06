using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ExactExceptionHandlerEffectTests
{
    [TestCase("catch (System.ArgumentException) { State = 1; }")]
    [TestCase("catch (System.ArgumentException) when ((State = 1) == 1) { State = 2; }")]
    public async Task ExactExceptionCannotExecuteSubtypeHandlerEffects(string handler)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[EnforcePure] public static void Target() { try { throw new System.Exception(); } " +
            handler + " catch (System.Exception) { } } }";
        AssertRuntimeReturnsWithoutWrite(source);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task ExactExceptionCannotExecuteThrowingSubtypeHandler()
    {
        const string source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[DoesNotThrow] public static void Target() { try { throw new System.Exception(); } " +
            "catch (System.ArgumentException) { throw null; } catch (System.Exception) { } } }";
        AssertRuntimeReturnsWithoutWrite(source);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single();
    }

    private static void AssertRuntimeReturnsWithoutWrite(string source)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("ExactHandlerRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ExactHandlerRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            Assert.DoesNotThrow(new Action(() => type.GetMethod("Target")!.Invoke(null, null)));
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(0));
        }
        finally { context.Unload(); }
    }
}
