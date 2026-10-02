using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeAllocationEffectTests
{
    [TestCase("return x;", true)]
    [TestCase("State = x; return x;", true)]
    [TestCase("new object(); return x;", false)]
    [TestCase("if (x == 0) new object(); return x;", false)]
    [TestCase("Contract.Requires(x != 0); if (x == 0) new object(); return x;", true)]
    [TestCase("new object(); throw null;", false)]
    [TestCase("while (x > 0) { new object(); x--; } return x;", false)]
    public async Task CapturedAllocationSitesQualifyAndMatchCompiledRuntime(string body, bool proven)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            "[ZeroAllocations, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "public static int Target(int x) { " + body + " } }";
        var preparation = Prepare(source);
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(native.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), native.Reason.ToString());
        Assert.That(native.AllocationWitness.HasValue, Is.EqualTo(!proven));
        if (!proven)
        {
            Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
                .OfType<IrAllocationInstruction>().Any(allocation => allocation.Operation == native.AllocationWitness), Is.True);
        }
        var input = (int)native.EntryModel.Values.Single().IntegerNumericValue;
        Assert.That(AllocatedBytes(source, input), proven ? Is.Zero : Is.GreaterThan(0));
    }

    [TestCase("public static object State = new object();", "return x;")]
    [TestCase("[System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { State = new object(); } public static object State;", "return x;")]
    [TestCase("", "return (\"x\" + x.ToString()).Length;")]
    [TestCase("", "return ((x == 0 ? \"a\" : \"b\") + \"c\").Length;")]
    [TestCase("", "throw null;")]
    [TestCase("", "try { return 10 / x; } catch (System.DivideByZeroException) { return x; }")]
    [TestCase("", "new System.Text.StringBuilder(); return x;")]
    public async Task MissingImplicitAllocationOrInitializationRowsAbstain(string members, string body)
    {
        var preparation = Prepare("using SharpProof.Attributes; public static class C { " + members +
            " [ZeroAllocations] public static int Target(int x) { " + body + " } }");
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(native.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That(native.AllocationWitness, Is.Null);
    }

    [Test]
    public async Task ValidGenericReferenceClaimSurvivesLegacyLanguageAdmission()
    {
        var preparation = Prepare("using SharpProof.Attributes; public class Node<T> {} public static class C { " +
            "[ZeroAllocations] public static bool Target<T>(Node<T> node) => node == null; }");
        Assert.That(preparation.Total!.ValidEffectClaimIds, Does.Contain(preparation.EffectClaims.Single().ClaimId));
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>(), native.Reason.ToString());
    }

    [TestCase("foreign-claim")]
    [TestCase("duplicate-claim")]
    [TestCase("null-claims")]
    [TestCase("unused-slot")]
    [TestCase("invalid-type")]
    [TestCase("scalar-type")]
    public void AllocationDecoderRejectsUnownedClaimsAndMalformedEvents(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ZeroAllocations] public static int Target(int x) { new object(); return x; } }");
        var total = artifact.Callables.Single().Total!;
        var allocation = total.Graph.Blocks.SelectMany(block => block.Instructions).Single(row => row.Kind == IrInstructionKind.Allocate);
        switch (mutation)
        {
            case "foreign-claim":
                total.ValidEffectClaimIds = ["foreign"];
                break;
            case "duplicate-claim":
                total.ValidEffectClaimIds = [total.ValidEffectClaimIds.Single(), total.ValidEffectClaimIds.Single()];
                break;
            case "null-claims":
                total.ValidEffectClaimIds = null!;
                break;
            case "unused-slot":
                allocation.B = 0;
                break;
            case "invalid-type":
                allocation.A = -1;
                break;
            case "scalar-type":
                allocation.A = 0;
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<System.Text.Json.JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingAllocationAdmissionAbstainsEvenAtContradictoryEntry(bool initialization)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ZeroAllocations] public static int Target(int x) { Contract.Requires(false); new object(); return x; } }");
        var total = artifact.Callables.Single().Total!;
        if (initialization)
        { total.EffectsCompleteAtEntry = false; }
        else
        { total.ValidEffectClaimIds = []; }
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets());
        Assert.That(native.Outcome, Is.Null);
        Assert.That(native.Reason, Is.EqualTo(initialization ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.UnsupportedContract));
    }

    [Test]
    public async Task CaughtFaultAllocationIsConfirmedByCompiledCSharp()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ZeroAllocations, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "public static int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) { return x; } } }";
        var native = await NativeEffectSiteVerifier.VerifyAsync(Prepare(source), new WorkerBudgets());
        Assert.That(native.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That(AllocatedBytes(source, 0), Is.GreaterThan(0));
    }

    [TestCase("scalar-type")]
    [TestCase("foreign-type")]
    [TestCase("foreign-site")]
    public void AllocationEventsRequireAnOwnedReferenceTypeAndSite(string mutation)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var foreign = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var type = mutation == "scalar-type" ? factory.IntegerType : mutation == "foreign-type" ? foreign.ObjectType : factory.ObjectType;
        var site = mutation == "foreign-site" ? foreign.CreateOperation("foreign") : factory.CreateOperation("owned");
        Assert.Throws<ArgumentException>(new Action(() => builder.Allocate(block, site, type)));
    }

    private static long AllocatedBytes(string source, int input)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("NativeAllocationRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("NativeAllocationOracle", isCollectible: true);
        try
        {
            var method = context.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
            var run = method.CreateDelegate<Func<int, int>>();
            // Create delegates and warm JIT/exception machinery before measuring.
            for (var repeat = 0; repeat < 3; repeat++)
            { try { run(input); } catch (NullReferenceException) { } }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 32; repeat++)
            { try { run(input); } catch (NullReferenceException) { } }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally { context.Unload(); }
    }

    [Test]
    public async Task SourceHelperAllocationSurvivesExpansionAndMatchesRuntime()
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "private static int Helper(int value) { new object(); value++; return value; } " +
            "[ZeroAllocations] public static int Target(int x) { return Helper(x); } }";
        var preparation = Prepare(source);
        var result = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.AllocationWitness, Is.Not.Null);
        Assert.That(AllocatedBytes(source, (int)result.EntryModel.Values.Single().IntegerNumericValue), Is.GreaterThan(0));
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        return preparations.Single();
    }
}
