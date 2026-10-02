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
    [TestCase("System.Action action = new System.Action(Sink); return x;", false)]
    [TestCase("if (x == 0) { System.Action action = new System.Action(Sink); } return x;", false)]
    [TestCase("Contract.Requires(x != 0); if (x == 0) { System.Action action = new System.Action(Sink); } return x;", true)]
    [TestCase("while (x > 0) { System.Action action = new System.Action(Sink); x--; } return x;", false)]
    [TestCase("object value = x; return x;", false)]
    [TestCase("object value = (object)(x > 0); return x;", false)]
    [TestCase("object value = new object(); return x;", false)]
    [TestCase("if (x == 0) { object value = x; } return x;", false)]
    [TestCase("Contract.Requires(x != 0); if (x == 0) { object value = x; } return x;", true)]
    [TestCase("while (x > 0) { object value = x; x--; } return x;", false)]
    [TestCase("if (x == 0) new object(); return x;", false)]
    [TestCase("Contract.Requires(x != 0); if (x == 0) new object(); return x;", true)]
    [TestCase("new object(); throw null;", false)]
    [TestCase("while (x > 0) { new object(); x--; } return x;", false)]
    public async Task CapturedAllocationSitesQualifyAndMatchCompiledRuntime(string body, bool proven)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; private static void Sink() {} " +
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

    [TestCase("System.Action action = Sink;")]
    [TestCase("System.Action action = () => {};")]
    [TestCase("System.Action action = () => { State = x; };")]
    [TestCase("System.Action action = new System.Action(() => {});")]
    [TestCase("System.Action action = new System.Action(instance.Sink);")]
    [TestCase("System.Action action = new System.Action(Generic<int>);")]
    public async Task CachedCapturingAndReceiverDependentDelegatesStayUnmodeled(string body)
    {
        var preparation = Prepare("using SharpProof.Attributes; public class Receiver { public void Sink() {} } public static class C { " +
            "public static int State; private static void Sink() {} private static void Generic<T>() {} " +
            "[ZeroAllocations] public static int Target(int x, Receiver instance) { " + body + " return x; } }");
        var result = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That(result.AllocationWitness, Is.Null);
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
                allocation.C = 0;
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

    [TestCase("invalid-index")]
    [TestCase("wrong-type")]
    [TestCase("unused-slot")]
    public void AllocationValueDecoderRejectsMalformedTargets(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ZeroAllocations] public static object Target(int x) { return (object)x; } }");
        var graph = artifact.Callables.Single().Total!.Graph;
        var allocation = graph.Blocks.SelectMany(block => block.Instructions).Single(row => row.Kind == IrInstructionKind.Allocate);
        if (mutation == "invalid-index")
        { allocation.B = int.MaxValue; }
        else if (mutation == "wrong-type")
        { allocation.B = 0; }
        else
        { allocation.C = 0; }
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

    [TestCase("x")]
    [TestCase("new object()")]
    public async Task AllocationValuesHaveFreshNonnullIdentitiesAndMatchRuntime(string value)
    {
        var source = "using SharpProof.Attributes; public static class C { [EnforcePure, ZeroAllocations] " +
            "public static bool Target(object input, int x) { Contract.Ensures(Contract.Result<bool>()); " +
            "object first = " + value + "; object second = " + value + "; return first != null && first != input && first != second; } }";
        var preparation = Prepare(source);
        Assert.That((await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That((await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure),
            Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAllocationInstruction>()
            .All(allocation => allocation.Target != null), Is.True);
        using var image = new MemoryStream();
        var runtimeSource = source.Replace("Contract.Ensures(Contract.Result<bool>()); ", "", StringComparison.Ordinal);
        Assert.That(TestCompilation.Create("AllocationIdentityRuntime", runtimeSource).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("AllocationIdentityRuntime", isCollectible: true);
        try
        {
            var run = runtime.LoadFromStream(image).GetType("C")!.GetMethod("Target")!.CreateDelegate<Func<object, int, bool>>();
            Assert.That(run(new object(), 1), Is.True);
            Assert.That(run(null!, 0), Is.True);
        }
        finally { runtime.Unload(); }
    }

    [Test]
    public void BoxingEvaluatesFaultingOperandBeforeAllocation()
    {
        var preparation = Prepare("using SharpProof.Attributes; public static class C { [ZeroAllocations] " +
            "public static object Target(int x) { return (object)(10 / x); } }");
        var total = preparation.Total!;
        var allocations = 0;
        var execution = new IrProgramInterpreter(total.Program.Factory).Execute(total.Program,
            new Dictionary<IrVarId, IrValue>
            {
                [total.Parameters.Single().Entry] = total.Program.Factory.CreateIntegerValue(total.Program.Factory.IntegerType, 0L)
            }, 10000, new IrProgramReplayOptions(_ => null) { AllocationObserver = _ => allocations++ });
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(allocations, Is.Zero);
    }

    [Test]
    public void AllocationTargetsRequireMatchingOwnedReferenceStorage()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var foreign = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("allocation");
        Assert.Throws<ArgumentException>(new Action(() => builder.Allocate(block, site, factory.ObjectType,
            factory.CreateVariable("wrong", factory.IntegerType))));
        Assert.Throws<ArgumentException>(new Action(() => builder.Allocate(block, site, factory.ObjectType,
            foreign.CreateVariable("foreign", foreign.ObjectType))));
    }

    [TestCase("new object();")]
    [TestCase("System.Action action = new System.Action(Sink);")]
    public async Task SourceHelperAllocationSurvivesExpansionAndMatchesRuntime(string allocation)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "private static int Helper(int value) { " + allocation + " value++; return value; } private static void Sink() {} " +
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
