using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ExactExceptionHandlerControlTests
{
    [Test]
    public async Task VariableExceptionMayExecuteSubtypeHandlerEffects()
    {
        const string source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[EnforcePure] public static void Target(System.Exception error) { if (error == null) return; " +
            "try { throw error; } catch (System.ArgumentException) { State = 1; } catch (System.Exception) { } } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("VariableHandlerRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("VariableHandlerRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            type.GetMethod("Target")!.Invoke(null, [new ArgumentException()]);
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(1));
        }
        finally { context.Unload(); }
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparations.Single(), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }
}
