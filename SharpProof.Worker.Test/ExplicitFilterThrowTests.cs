using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ExplicitFilterThrowTests
{
    [TestCase("new System.ArgumentException()")]
    [TestCase("new System.InvalidOperationException(\"filter\")")]
    [TestCase("null")]
    public async Task ThrowingFilterRejectsHandlerAndPreservesOriginalException(string exception)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            "[DoesNotThrow] public static void Target(bool flag) { " +
            "try { throw new System.DivideByZeroException(); } " +
            "catch (System.DivideByZeroException) when (flag ? throw " + exception + " : false) { throw null; } " +
            "catch (System.DivideByZeroException) { } } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("FilterThrowRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("FilterThrowRuntime", isCollectible: true);
        try
        {
            var target = context.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
            Assert.DoesNotThrow(new Action(() => target.Invoke(null, [true])));
            Assert.DoesNotThrow(new Action(() => target.Invoke(null, [false])));
        }
        finally { context.Unload(); }
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }
}
