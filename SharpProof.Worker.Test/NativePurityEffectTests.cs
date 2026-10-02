using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativePurityEffectTests
{
    [TestCase("var y = x; y++; x += y; return x;")]
    [TestCase("while (x > 0) x--; return x;")]
    [TestCase("new object(); return x;")]
    [TestCase("return x++;")]
    public async Task SourceLocalMutationsAndFreshAllocationsRemainPure(string body)
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(Source(body)));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Null);
    }

    [Test]
    public async Task SourceHelpersPreserveLocalWritesAndFreshAllocations()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(Source("return Helper(x);",
            "private static int Helper(int value) { new object(); value++; return value; }")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrWriteInstruction>(), Is.Not.Empty);
        Assert.That(preparation.Total.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAllocationInstruction>(), Is.Not.Empty);
    }

    [TestCase(IrWriteRegion.Local, true)]
    [TestCase(IrWriteRegion.Parameter, false)]
    [TestCase(IrWriteRegion.Field, false)]
    [TestCase(IrWriteRegion.Static, false)]
    [TestCase(IrWriteRegion.Element, false)]
    [TestCase(IrWriteRegion.Unknown, false)]
    public async Task CanonicalWriteEventsUseGuardedReachabilityAndOriginalReplay(IrWriteRegion region, bool pure)
    {
        // Exercise the portable event protocol independently of source-region
        // classification; only local source assignments are admitted so far.
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source("if (x == 0) x = 1; return x;"));
        var row = artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block.Instructions)
            .Single(instruction => instruction.Kind == IrInstructionKind.Write);
        row.A = (int)region;
        var preparation = Prepare(artifact);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, pure ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness.HasValue, Is.EqualTo(!pure));
        if (!pure)
        {
            Assert.That((int)result.EntryModel.Values.Single().IntegerNumericValue, Is.Zero);
            Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrWriteInstruction>()
                .Any(write => write.Operation == result.WriteWitness && write.Region == region), Is.True);
        }
    }

    [Test]
    public async Task PreconditionsCanExcludeNonlocalWriteEvents()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source("Contract.Requires(x != 0); if (x == 0) x = 1; return x;"));
        artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block.Instructions)
            .Single(instruction => instruction.Kind == IrInstructionKind.Write).A = (int)IrWriteRegion.Field;
        Assert.That((await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets())).Outcome,
            Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task LoopWriteWitnessRequiresOriginalBodyReplay()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source("while (x > 0) x--; return x;"));
        artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block.Instructions)
            .Single(instruction => instruction.Kind == IrInstructionKind.Write).A = (int)IrWriteRegion.Element;
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Not.Null);
        Assert.That((int)result.EntryModel.Values.Single().IntegerNumericValue, Is.GreaterThan(0));
    }

    [TestCase(-1, false)]
    [TestCase(6, false)]
    [TestCase(0, true)]
    public void MalformedWriteRegionsAndUnusedSlotsAreRejected(int region, bool unusedSlot)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source("x = 1; return x;"));
        var row = artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block.Instructions)
            .Single(instruction => instruction.Kind == IrInstructionKind.Write);
        row.A = region;
        if (unusedSlot)
        { row.B = 0; }
        Assert.Throws<System.Text.Json.JsonException>(new Action(() => Prepare(artifact)));
    }

    [TestCase("public static int State;", "State = x; return x;")]
    [TestCase("", "System.Console.WriteLine(x); return x;")]
    [TestCase("public static object State = new object();", "return x;")]
    public async Task UnmodeledEffectsAndInitializationRemainUnknown(string members, string body)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source(body, members));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingAdmissionOrInitializationAbstainsBeforeVacuousPurity(bool initialization)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source("Contract.Requires(false); return x;"));
        if (initialization)
        { artifact.Callables.Single().Total!.EffectsCompleteAtEntry = false; }
        else
        { artifact.Callables.Single().Total!.ValidEffectClaimIds = []; }
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(initialization ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.UnsupportedContract));
    }

    private static string Source(string body, string members = "")
    { return "using SharpProof.Attributes; public static class C { " + members + " [EnforcePure] public static int Target(int x) { " + body + " } }"; }

    private static CompilerCallablePreparation Prepare(CompilerManifestArtifact artifact)
    {
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single();
    }
}
