using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An EffectContract summary must cover every effect of the body. A field
// store needs its own region flag when its owner is known; unknown field and
// element stores may reach any state and need every flag of their kind.
[TestFixture]
public sealed class NativeEffectContractTests
{
    private const string Cell = "public class Cell { public int Value; } ";
    private const string Reads = "SharpProofEffect.ReadsReceiverState | SharpProofEffect.ReadsArgumentState | " +
        "SharpProofEffect.ReadsCapturedState | SharpProofEffect.ReadsStaticState | SharpProofEffect.ReadsAmbientState";
    private const string Writes = "SharpProofEffect.WritesReceiverState | SharpProofEffect.WritesArgumentState | " +
        "SharpProofEffect.WritesCapturedState | SharpProofEffect.WritesStaticState | SharpProofEffect.WritesAmbientState";

    [Test]
    public async Task DirectReceiverWriteRespectsReceiverOnlyEffectContract()
    {
        const string method = "public int Stored; [EffectContract(SharpProofEffect.WritesReceiverState)] " +
            "public int Target(int value) { Stored = value; return value; }";
        const string source = "using SharpProof.Attributes; public sealed class C { " + method + " }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("ReceiverWriteOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("receiver-write-oracle", isCollectible: true);
        try
        {
            var instance = Activator.CreateInstance(context.LoadFromStream(image).GetType("C")!)!;
            var result = instance.GetType().GetMethod("Target")!.Invoke(instance, [7]);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.EqualTo(7));
                Assert.That(instance.GetType().GetField("Stored")!.GetValue(instance), Is.EqualTo(7));
            }
        }
        finally { context.Unload(); }
        var verified = await VerifyAsync(method);
        Assert.That(verified.Outcome, Is.TypeOf<ProvenOutcome>(), verified.Reason.ToString());
    }

    [Test]
    public async Task InlinedCalleeReceiverIsNotCallerReceiver()
    {
        const string method = "[EffectContract(SharpProofEffect.WritesReceiverState)] " +
            "public int Target([NotNull] C other, int value) { other.Set(value); return value; }";
        const string source = "using SharpProof.Attributes; public sealed class C { " +
            "public int Stored; private void Set(int value) { Stored = value; } " + method + " }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("OtherReceiverOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("other-receiver-oracle", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var receiver = Activator.CreateInstance(type)!;
            var other = Activator.CreateInstance(type)!;
            Assert.That(type.GetMethod("Target")!.Invoke(receiver, [other, 7]), Is.EqualTo(7));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(type.GetField("Stored")!.GetValue(receiver), Is.EqualTo(0));
                Assert.That(type.GetField("Stored")!.GetValue(other), Is.EqualTo(7));
            }
        }
        finally { context.Unload(); }
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single(item => item.EffectClaims.Length != 0);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Any(write => write.Region == IrWriteRegion.Field && write.Field != null), Is.True,
            "The source callee must be inlined as a modeled field store.");
        var verified = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets());
        Assert.That(verified.Outcome, Is.TypeOf<RefutedOutcome>(), verified.Reason.ToString());
        Assert.That(verified.WriteWitness, Is.Not.Null);
    }

    [TestCase("SharpProofEffect.None", "Stored = value;", typeof(RefutedOutcome))]
    [TestCase("SharpProofEffect.WritesReceiverState", "var self = this; self.Stored = value;", typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.WritesReceiverState", "cell.Value = value;", typeof(RefutedOutcome))]
    [TestCase("SharpProofEffect.WritesReceiverState", "var other = cell; other.Value = value;", null)]
    [TestCase("SharpProofEffect.WritesReceiverState", "C.Shared = value;", typeof(RefutedOutcome))]
    public async Task ReceiverOnlySummaryDistinguishesKnownOwners(string effects, string body, Type? outcome)
    {
        var result = await VerifyAsync("public int Stored; public static int Shared; [EffectContract(" + effects +
            ")] public int Target(Cell cell, int value) { " + body + " return value; }");
        if (outcome != null)
        { Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString()); }
        else
        { Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>()); }
    }

    [TestCase("SharpProofEffect.None", "return value + 1;", typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.None", "Contract.Requires(cell != null); return cell.Value;", null)]
    [TestCase(Reads, "Contract.Requires(cell != null); return cell.Value;", typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.None", "Contract.Requires(cell != null); cell.Value = value; return 0;", typeof(RefutedOutcome))]
    [TestCase("SharpProofEffect.WritesArgumentState", "Contract.Requires(cell != null); cell.Value = value; return 0;", typeof(ProvenOutcome))]
    // Creating the object allocates, which the summary does not declare.
    [TestCase(Writes, "var other = new Cell(); other.Value = value; return 0;", typeof(RefutedOutcome))]
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

    // Reading the length or an element of an argument's sequence reads
    // argument state; reading a sequence the body created does not.
    [TestCase("SharpProofEffect.None", "return values.Length;", false)]
    [TestCase("SharpProofEffect.None", "return text.Length > 0 ? text[0] : 0;", false)]
    [TestCase(Reads, "return values.Length;", true)]
    [TestCase("SharpProofEffect.Allocates", "var local = new int[3]; return local.Length + local[0];", true)]
    [TestCase("SharpProofEffect.None", "return \"abc\".Length;", true)]
    public async Task SequenceReadsNeedReadEffects(string effects, string body, bool proven)
    {
        var result = await VerifyAsync("[EffectContract(" + effects + ", Complete = true)] public int Target(" +
            "[NotNull] int[] values, [NotNull] string text) { " + body + " }");
        if (proven)
        { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        else
        { Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>()); }
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
