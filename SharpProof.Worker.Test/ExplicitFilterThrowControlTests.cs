using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ExplicitFilterThrowControlTests
{
    [TestCase("[DoesNotThrow]", "true", "throw null;", false)]
    [TestCase("[DoesNotThrow]", "true ? throw new System.ArgumentException() : false", "", true)]
    [TestCase("[EnforcePure]", "(State = 1) == 1 ? throw new System.ArgumentException() : false", "", false)]
    [TestCase("[DoesNotThrow]", "true ? throw new System.ArgumentException() : false", "", false, false)]
    public async Task FilterWritesAndEscapingOriginalExceptionsRemainObservable(
        string attribute, string filter, string handler, bool proven, bool catchOriginal = true)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            attribute + " public static void Target() { try { throw new System.DivideByZeroException(); } " +
            "catch (System.DivideByZeroException) when (" + filter + ") { " + handler + " } " +
            (catchOriginal ? "catch (System.DivideByZeroException) { }" : "catch (System.ArgumentException) { }") + " } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("FilterControlRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("FilterControlRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            if (filter == "true" || !catchOriginal)
            {
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => type.GetMethod("Target")!.Invoke(null, null)));
                Assert.That(error!.InnerException, filter == "true" ? Is.TypeOf<NullReferenceException>() : Is.TypeOf<DivideByZeroException>());
            }
            else
            {
                Assert.DoesNotThrow(new Action(() => type.GetMethod("Target")!.Invoke(null, null)));
                Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(attribute == "[EnforcePure]" ? 1 : 0));
            }
        }
        finally { context.Unload(); }
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var result = attribute == "[EnforcePure]"
            ? await NativeEffectSiteVerifier.VerifyPurityAsync(preparations.Single(), new WorkerBudgets())
            : await NativeExceptionEffectVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets());
        Assert.That(result.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
    }
}
