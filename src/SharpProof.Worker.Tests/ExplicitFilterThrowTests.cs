using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ExplicitFilterThrowTests
{
    [TestCase("new System.ArgumentException()", false)]
    [TestCase("new System.ArgumentException(\"filter\")", false)]
    [TestCase("new System.InvalidOperationException(\"filter\")", false)]
    [TestCase("null", false)]
    [TestCase("new System.ArgumentException()", true)]
    [TestCase("new System.ArgumentException(\"filter\")", true)]
    [TestCase("new System.InvalidOperationException(\"filter\")", true)]
    [TestCase("null", true)]
    public async Task ThrowingFilterRejectsHandlerAndPreservesOriginalException(string exception, bool explicitOriginal)
    {
        System.ArgumentNullException.ThrowIfNull(exception);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[DoesNotThrow] public static void Target(bool flag) { " +
            "try { throw " + (explicitOriginal ? "new System.DivideByZeroException(\"original\")" : "new System.DivideByZeroException()") + "; } " +
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
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, explicitOriginal ? Is.TypeOf<ProvenOutcome>() : Is.Null, result.Reason.ToString());
    }
}
