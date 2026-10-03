using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An EffectContract summary must cover every effect of the body. A field or
// element access may reach any state, so it needs every flag of its kind.
[TestFixture]
public sealed class NativeEffectContractTests
{
    private const string Cell = "public class Cell { public int Value; } ";
    private const string Reads = "SharpProofEffect.ReadsReceiverState | SharpProofEffect.ReadsArgumentState | " +
        "SharpProofEffect.ReadsCapturedState | SharpProofEffect.ReadsStaticState | SharpProofEffect.ReadsAmbientState";
    private const string Writes = "SharpProofEffect.WritesReceiverState | SharpProofEffect.WritesArgumentState | " +
        "SharpProofEffect.WritesCapturedState | SharpProofEffect.WritesStaticState | SharpProofEffect.WritesAmbientState";

    [TestCase("SharpProofEffect.None", "return value + 1;", typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.None", "Contract.Requires(cell != null); return cell.Value;", null)]
    [TestCase(Reads, "Contract.Requires(cell != null); return cell.Value;", typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.None", "Contract.Requires(cell != null); cell.Value = value; return 0;", typeof(RefutedOutcome))]
    [TestCase("SharpProofEffect.WritesArgumentState", "Contract.Requires(cell != null); cell.Value = value; return 0;", typeof(ProvenOutcome))]
    [TestCase(Writes, "var other = new Cell(); other.Value = value; return 0;", null)]
    [TestCase("SharpProofEffect.None", "var box = new object(); return 0;", typeof(RefutedOutcome))]
    [TestCase("SharpProofEffect.Allocates", "var box = new object(); return 0;", typeof(ProvenOutcome))]
    public async Task SummariesBoundReadsWritesAndAllocations(string effects, string body, Type? outcome)
    {
        var result = await VerifyAsync("[EffectContract(" + effects + ")] public int Target(Cell cell, int value) { " + body + " }");
        if (outcome != null)
        { Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString()); }
        else
        {
            Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(result.Outcome, Is.Not.TypeOf<RefutedOutcome>());
        }
    }

    // A thrown exception needs Throws and a listed type; creating it allocates.
    [TestCase("SharpProofEffect.Throws | SharpProofEffect.Allocates, ThrownExceptions = new[] { typeof(System.InvalidOperationException) }",
        typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.Allocates", typeof(RefutedOutcome))]
    [TestCase("SharpProofEffect.Throws | SharpProofEffect.Allocates, ThrownExceptions = new[] { typeof(System.ArgumentException) }",
        typeof(RefutedOutcome))]
    public async Task ThrowsNeedDeclaredTypes(string effects, Type outcome)
    {
        var result = await VerifyAsync("[EffectContract(" + effects + ")] public void Target(bool flag) { " +
            "if (flag) throw new System.InvalidOperationException(); }");
        Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString());
    }

    private static async Task<PassiveCallableCheckResult> VerifyAsync(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; " + Cell + "public sealed class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return await NativeEffectSiteVerifier.VerifyEffectContractAsync(
            preparations.Single(preparation => preparation.EffectClaims.Length != 0), new WorkerBudgets());
    }
}
