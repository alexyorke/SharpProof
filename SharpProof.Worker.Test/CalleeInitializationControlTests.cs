using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;
namespace SharpProof.Worker.Test;
[TestFixture]
public sealed class CalleeInitializationControlTests
{
    [TestCase("public static int State = 1;")]
    [TestCase("static C() { }")]
    public async Task ExistingRootInitializationAdmissionIsClosed(string members)
    {
        var source = "using SharpProof.Attributes; public static class C { " + members + " [EnforcePure] public static int Target(int x) { return x; } }";
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [TestCase("public static int State = 1;", WorkerClaimOutcome.Unknown)]
    [TestCase("public static readonly int State = 1;", WorkerClaimOutcome.Proven)]
    [TestCase("static Other() { }", WorkerClaimOutcome.Unknown)]
    [TestCase("", WorkerClaimOutcome.Proven)]
    public async Task WorkerPreservesFunctionalInliningAndChecksInitializationEffects(string field, WorkerClaimOutcome pure)
    {
        var source = "using SharpProof.Attributes; public static class Other { " + field + " public static int Helper(int x) { return x; } } public static class C { [EnforcePure] public static int Target(int x) { Contract.Ensures(Contract.Result<int>() == x); return Other.Helper(x); } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var callable = project.Snapshot.CompilerManifest.Callables.Single();
        var purity = callable.EffectClaims.Single().ClaimId;
        Assert.That(response.ClaimResults.Single(row => row.ClaimId == purity).Outcome, Is.EqualTo(pure));
        if (field != "static Other() { }")
        { Assert.That(response.ClaimResults.Single(row => row.ClaimId != purity).Outcome, Is.EqualTo(WorkerClaimOutcome.Proven)); }
    }

    [Test]
    public void MutableConstantInitializerContainsAnObservableStaticStore()
    {
        const string source = "public static class Other { public static int State = 1; public static int Helper(int x) { return x; } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("CalleeInitializationRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("CalleeInitializationRuntime", isCollectible: true);
        try
        {
            var type = runtime.LoadFromStream(image).GetType("Other")!;
            Assert.That(type.Attributes.HasFlag(System.Reflection.TypeAttributes.BeforeFieldInit), Is.True);
            Assert.That(type.TypeInitializer, Is.Not.Null);
            Assert.That(type.TypeInitializer!.GetMethodBody()!.GetILAsByteArray(), Does.Contain((byte)0x80));
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(1));
            Assert.That(type.GetMethod("Helper")!.CreateDelegate<Func<int, int>>()(7), Is.EqualTo(7));
        }
        finally { runtime.Unload(); }
    }

    [TestCase("public static int State;", "public static int Helper(int x) => x;", "Other.Helper(x)", true)]
    [TestCase("public static int State = 1;", "public int Helper(int x) => x;", "new Other().Helper(x)", false)]
    [TestCase("public static readonly int State = 1;", "public int Helper(int x) => x;", "new Other().Helper(x)", true)]
    [TestCase("public static int State = 1;", "", "new Other(); return x", false)]
    [TestCase("public static readonly int State = 1;", "", "new Other(); return x", true)]
    public async Task MemberAndImplicitConstructorInitializationIsPreserved(string field, string helper, string body, bool pure)
    {
        var expression = body?.StartsWith("new Other();", StringComparison.Ordinal) == true ? body : "return " + body;
        var source = "using SharpProof.Attributes; public sealed class Other { " + field + " " + helper + " } public static class C { [EnforcePure] public static int Target(int x) { " + expression + "; } }";
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(source), new WorkerBudgets());
        Assert.That(result.Outcome, pure ? Is.TypeOf<ProvenOutcome>() : Is.Null);
    }
    private static CompilerCallablePreparation Prepare(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single();
    }
}
