using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;
namespace SharpProof.Worker.Test;
[TestFixture]
public sealed class CalleeInitializationAuditTests
{
    [TestCase("public static int State = 1;", false)]
    [TestCase("public static readonly int State = 1;", true)]
    [TestCase("", true)]
    public async Task CalleeInitializationIsIncludedInPurityAdmission(string field, bool pure)
    {
        var source = "using SharpProof.Attributes; public static class Other { " + field + " public static int Helper(int x) { return x; } } public static class C { [EnforcePure] public static int Target(int x) { return Other.Helper(x); } }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparations.Single(), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (pure)
        { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        else
        { Assert.That(result.Outcome, Is.Null, "Mutable callee type initialization cannot be proven pure."); }
    }
}
