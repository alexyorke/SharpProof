using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An API specification narrows an opaque call to its declared facets; a call
// without one, or a dispatched call, may do anything.
[TestFixture]
public sealed class NativeSpecifiedCallTests
{
    [Test]
    public async Task SpecifiedPureCallsProveEveryEffectClaim()
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations, EnforcePure, AllowedCapabilities(SharpProofCapability.None)] " +
            "public static bool Target(string text) { return string.IsNullOrEmpty(text); }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        var capabilities = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            foreach (var result in new[] { exceptions, allocations, purity, capabilities })
            { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        }
    }

    [TestCase("System.Console.WriteLine();")]
    [TestCase("var text = value.ToString();")]
    public async Task UnspecifiedOrDispatchedCallsMayUseAnyCapability(string body)
    {
        var preparation = Prepare("[AllowedCapabilities(SharpProofCapability.None)] public static void Target(object value) { " +
            "Contract.Requires(value != null); " + body + " }");
        var result = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That(result.Outcome, Is.Not.TypeOf<RefutedOutcome>());
    }

    [TestCase("SharpProofCapability.None", typeof(RefutedOutcome))]
    [TestCase("SharpProofCapability.Synchronization", typeof(ProvenOutcome))]
    public async Task LocksUseSynchronization(string allowed, Type outcome)
    {
        var preparation = Prepare("[AllowedCapabilities(" + allowed + ")] public static void Target() { lock (new object()) { } }");
        var result = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString());
    }

    private static CompilerCallablePreparation Prepare(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.EffectClaims.Length != 0);
    }
}
