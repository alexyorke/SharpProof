using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Type-parameter values are opaque: stored, passed, returned and type-tested,
// with no operator or conversion applied. Body defaults are approximations.
[TestFixture]
public sealed class NativeTypeParameterTests
{
    [TestCase("")]
    [TestCase("where T : class")]
    [TestCase("where T : struct")]
    [TestCase("where T : unmanaged")]
    [TestCase("where T : new()")]
    public async Task GenericDefaultsNeitherThrowNorAllocateNorWrite(string constraint)
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations, EnforcePure] public static T Target<T>() " + constraint + " { return default(T); }");
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.TypeOf<ProvenOutcome>(), exceptions.Reason.ToString());
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>(), purity.Reason.ToString());
        }
    }

    [TestCase("System.DateTime")]
    [TestCase("int?")]
    public async Task OpaqueStructDefaultsHaveNoEffects(string type)
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations, EnforcePure] public static " + type + " Target() => default(" + type + ");");
        Assert.That(preparation.Total, Is.Not.Null);
        var instructions = preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions).ToArray();
        Assert.That(instructions.OfType<IrHavocInstruction>().Any(havoc => havoc.Origin == IrHavocOrigin.Approximation), Is.True);
        Assert.That(instructions.Any(instruction => instruction is IrCallInstruction or IrAllocationInstruction or IrWriteInstruction), Is.False);
        Assert.That((await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AllocationWitnessRespectsGenericDefaultReadOrder(bool defaultFirst)
    {
        var approximation = "T ignored = default(T); ";
        var allocation = "var items = new int[1]; ";
        var preparation = Prepare("[ZeroAllocations] public static int Target<T>() { " +
            (defaultFirst ? approximation + allocation : allocation + approximation) + "return items.Length; }");
        var result = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        if (defaultFirst)
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(result.AllocationWitness, Is.Null);
        }
        else
        {
            Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
            Assert.That(result.AllocationWitness, Is.Not.Null);
        }
    }

    private const string IsNumber = """
        public static bool Target<T>(T value) {
            if (value is sbyte) return true;
            if (value is int) return true;
            if (value is ulong) return true;
            if (value is decimal) return true;
            return false;
        }
        """;

    [Test]
    public async Task TypeTestsNeitherThrowNorAllocateNorWrite()
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations, EnforcePure] " + IsNumber);
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exceptions.Outcome, Is.TypeOf<ProvenOutcome>(), exceptions.Reason.ToString());
            Assert.That(allocations.Outcome, Is.TypeOf<ProvenOutcome>(), allocations.Reason.ToString());
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>(), purity.Reason.ToString());
        }
    }

    [Test]
    public void TypeTestsOnTypeParametersDoNotAllocateAtRuntime()
    {
        // The ZeroAllocations proof relies on the JIT folding `box T; isinst`.
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("TypeTestRuntime",
            "using SharpProof.Attributes; public static class C { " + IsNumber + " }").Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("TypeTestRuntime", isCollectible: true);
        try
        {
            var generic = context.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(MeasureAllocation(generic, 5), Is.Zero);
                Assert.That(MeasureAllocation(generic, 5L), Is.Zero);
                Assert.That(MeasureAllocation(generic, 2.5), Is.Zero);
                Assert.That(MeasureAllocation(generic, "text"), Is.Zero);
            }
        }
        finally { context.Unload(); }
    }

    private static long MeasureAllocation<TValue>(System.Reflection.MethodInfo generic, TValue argument)
    {
        var target = generic.MakeGenericMethod(typeof(TValue)).CreateDelegate<Func<TValue, bool>>();
        for (var warmup = 0; warmup < 1000; warmup++)
        { _ = target(argument); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = target(argument);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [TestCase("return value;", true)]
    [TestCase("T copy = value; return copy;", true)]
    [TestCase("return default(T);", true)]
    [TestCase("return value == null ? value : value;", false)]
    public async Task OpaqueValuesFlowButOperatorsAbstain(string body, bool supported)
    {
        var preparation = Prepare("[DoesNotThrow] public static T Target<T>(T value) { " + body + " }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        if (supported)
        { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        else
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    private static CompilerCallablePreparation Prepare(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }

    [TestCase("", false)]
    [TestCase("where T : class", false)]
    [TestCase("where T : struct", false)]
    [TestCase("", true)]
    [TestCase("where T : class", true)]
    [TestCase("where T : struct", true)]
    public async Task GenericDefaultsDoNotInventPostconditionValues(string constraint, bool dependent)
    {
        var method = dependent
            ? "public static bool Target<T>() " + constraint + " { Contract.Ensures(Contract.Result<bool>()); T value = default(T); return value is object; }"
            : "public static int Target<T>(int value) " + constraint + " { Contract.Ensures(Contract.Result<int>() == value); T ignored = default(T); return value; }";
        var preparation = Prepare(method);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null, preparation.FailureReason.ToString());
        Assert.That(candidate!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>()
            .Any(havoc => havoc.Origin == IrHavocOrigin.Approximation), Is.True);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, dependent ? Is.TypeOf<UnknownOutcome>() : Is.TypeOf<ProvenOutcome>());
        Assert.That(result.Reason, Is.EqualTo(dependent ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.None));
    }

    [Test]
    public async Task DiscardedGenericDefaultDoesNotTaintIndependentRefutation()
    {
        var preparation = Prepare("public static int Target<T>(int value) { " +
            "Contract.Ensures(Contract.Result<int>() != value); _ = default(T); return value; }");
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
    }

    [TestCase("")]
    [TestCase("where T : class")]
    [TestCase("where T : struct")]
    public async Task GenericArrayDefaultsRemainExactNull(string constraint)
    {
        var preparation = Prepare("[DoesNotThrow, ZeroAllocations, EnforcePure] public static T[] Target<T>() " + constraint +
            " { Contract.Ensures(Contract.Result<T[]>() == null); return default(T[]); }");
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(candidate.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>()
            .Any(havoc => havoc.Origin == IrHavocOrigin.Approximation), Is.False);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task GenericDefaultConstructorArgumentsRetainFullBodyAdmission()
    {
        var preparation = Prepare("private sealed class Cell<T> { public Cell(T value) { } } " +
            "public static int Target<T>() { Contract.Ensures(Contract.Result<int>() == 7); " +
            "var items = new Cell<T>[1]; items[0] = new Cell<T>(default(T)); return 7; }");
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.False);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(candidate.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrHavocInstruction>()
            .Any(havoc => havoc.Origin == IrHavocOrigin.Approximation), Is.True);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public void GenericDefaultDoesNotInvokeExplicitStructConstructor()
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("DefaultRuntime", "public struct S { public int Value; " +
            "public S() { throw new System.InvalidOperationException(); } } " +
            "public static class C { public static T Target<T>() => default(T); }").Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("DefaultRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var type = assembly.GetType("S")!;
            var value = assembly.GetType("C")!.GetMethod("Target")!.MakeGenericMethod(type).Invoke(null, null);
            Assert.That(type.GetField("Value")!.GetValue(value), Is.EqualTo(0));
        }
        finally { context.Unload(); }
    }
}
