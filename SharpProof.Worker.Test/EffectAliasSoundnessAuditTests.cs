using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EffectAliasSoundnessAuditTests
{
    [Test]
    public async Task InlinedParameterStoreToCallerReceiverCannotUseArgumentOnlySummary()
    {
        const string source = "using SharpProof.Attributes; public sealed class C { " +
            "public int Stored; private static void Set(C target, int value) { target.Stored = value; } " +
            "[EffectContract(SharpProofEffect.WritesArgumentState)] " +
            "public int Target(int value) { Set(this, value); return value; } }";

        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("EffectAliasOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("effect-alias-oracle", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var receiver = Activator.CreateInstance(type)!;
            Assert.That(type.GetMethod("Target")!.Invoke(receiver, [7]), Is.EqualTo(7));
            Assert.That(type.GetField("Stored")!.GetValue(receiver), Is.EqualTo(7));
        }
        finally { context.Unload(); }

        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(item => item.EffectClaims.Length != 0);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Any(write => write.Region == IrWriteRegion.Parameter && write.Field != null),
            Is.True, "The source callee must be inlined as a modeled parameter-region field store.");
        var verified = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome, Is.TypeOf<RefutedOutcome>(), verified.Reason.ToString());
        Assert.That(verified.WriteWitness, Is.Not.Null);
    }

    [TestCase("SharpProofEffect.WritesReceiverState", true, typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.WritesArgumentState", false, typeof(ProvenOutcome))]
    [TestCase("SharpProofEffect.WritesReceiverState", false, typeof(RefutedOutcome))]
    [TestCase("SharpProofEffect.None", true, typeof(RefutedOutcome))]
    public async Task InlinedStoreFollowsCallerOwner(string effects, bool writeReceiver, Type expected)
    {
        var source = "using SharpProof.Attributes; public sealed class C { " +
            "public int Stored; private static void Set(C target, int value) { target.Stored = value; } " +
            "[EffectContract(" + effects + ")] " +
            "public int Target([NotNull] C other, int value) { Set(" +
            (writeReceiver ? "this" : "other") + ", value); return value; } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("EffectAliasControlOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("effect-alias-control-oracle", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var receiver = Activator.CreateInstance(type)!;
            var other = Activator.CreateInstance(type)!;
            Assert.That(type.GetMethod("Target")!.Invoke(receiver, [other, 7]), Is.EqualTo(7));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(type.GetField("Stored")!.GetValue(receiver), Is.EqualTo(writeReceiver ? 7 : 0));
                Assert.That(type.GetField("Stored")!.GetValue(other), Is.EqualTo(writeReceiver ? 0 : 7));
            }
            if (!writeReceiver)
            {
                Assert.That(type.GetMethod("Target")!.Invoke(receiver, [receiver, 11]), Is.EqualTo(11));
                Assert.That(type.GetField("Stored")!.GetValue(receiver), Is.EqualTo(11));
                Assert.That(type.GetField("Stored")!.GetValue(other), Is.EqualTo(7));
            }
        }
        finally { context.Unload(); }

        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(item => item.EffectClaims.Length != 0);
        var verified = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome, Is.TypeOf(expected), verified.Reason.ToString());
        if (expected == typeof(RefutedOutcome))
        { Assert.That(verified.WriteWitness, Is.Not.Null); }
    }

    [Test]
    public async Task InlinedReceiverStoreWitnessNamesReceiverEffect()
    {
        const string source = "using SharpProof.Attributes; public sealed class C { " +
            "public int Stored; private static void Set(C target, int value) { target.Stored = value; } " +
            "[EffectContract(SharpProofEffect.WritesArgumentState)] " +
            "public int Target(int value) { Set(this, value); return value; } }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(item => item.EffectClaims.Length != 0);
        var results = await NativeEffectClaims.VerifyAsync(
            preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        var claim = results.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(claim.EffectWitness, Is.Not.Null);
        Assert.That(claim.EffectWitness!.Kind, Is.EqualTo("nonlocal-write"));
        Assert.That(claim.EffectWitness.Effects, Is.EqualTo(WorkerEffectSet.WritesReceiverState));
        Assert.That(claim.EffectWitness.Location.Line, Is.GreaterThan(0));
        Assert.That(claim.EffectWitness.Location.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task ConditionalInlinedParameterOwnerCannotUseArgumentOnlySummary()
    {
        const string source = "using SharpProof.Attributes; public sealed class C { " +
            "public int Stored; private static void Set(C target, int value) { target.Stored = value; } " +
            "[EffectContract(SharpProofEffect.WritesArgumentState)] " +
            "public int Target([NotNull] C other, bool flag, int value) " +
            "{ Set(flag ? this : other, value); return value; } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("ConditionalEffectAliasOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("conditional-effect-alias-oracle", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var receiver = Activator.CreateInstance(type)!;
            var other = Activator.CreateInstance(type)!;
            Assert.That(type.GetMethod("Target")!.Invoke(receiver, [other, true, 7]), Is.EqualTo(7));
            Assert.That(type.GetField("Stored")!.GetValue(receiver), Is.EqualTo(7));
            Assert.That(type.GetField("Stored")!.GetValue(other), Is.EqualTo(0));
            var secondReceiver = Activator.CreateInstance(type)!;
            Assert.That(type.GetMethod("Target")!.Invoke(secondReceiver, [other, false, 9]), Is.EqualTo(9));
            Assert.That(type.GetField("Stored")!.GetValue(secondReceiver), Is.EqualTo(0));
            Assert.That(type.GetField("Stored")!.GetValue(other), Is.EqualTo(9));
        }
        finally { context.Unload(); }

        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(item => item.EffectClaims.Length != 0);
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.EffectsCompleteAtEntry, Is.True);
        Assert.That(preparation.Total.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Any(write => write.Region == IrWriteRegion.Parameter && write.Field != null),
            Is.True, "The helper write must stay a modeled field store.");
        var verified = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome, Is.TypeOf<RefutedOutcome>(), verified.Reason.ToString());
        Assert.That(verified.WriteWitness, Is.Not.Null);
    }
}
