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
    [TestCase("object value = x; return x;")]
    [TestCase("object value = new object(); return x;")]
    [TestCase("return x++;")]
    public async Task SourceLocalMutationsAndFreshAllocationsRemainPure(string body)
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(Source(body)));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Null);
    }

    [Test]
    public async Task ExplicitDelegateConstructionDoesNotExecuteItsImpureTarget()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(Source(
            "System.Action action = new System.Action(Sink); return x;",
            "private static void Sink() { System.Console.WriteLine(1); }")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAllocationInstruction>().Single().Target, Is.Not.Null);
    }

    [Test]
    public async Task DelegateConstructionDoesNotRunTargetTypeInitialization()
    {
        var source = Source("System.Action action = new System.Action(Other.Sink); return x;",
            "public static int State; private static class Other { static Other() { State = 1; } public static void Sink() { State = 2; } }");
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("DelegateInitializationRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("DelegateInitializationRuntime", isCollectible: true);
        try
        {
            var type = runtime.LoadFromStream(image).GetType("C")!;
            var run = type.GetMethod("Target")!.CreateDelegate<Func<int, int>>();
            Assert.That(run(7), Is.EqualTo(7));
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(0));
        }
        finally { runtime.Unload(); }
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
        // classification independently of the admitted source stores.
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

    [TestCase("public static volatile int State;", "State = x; return x;")]
    [TestCase("public static int State;", "State = x; return State;")]
    [TestCase("public static int State = 1;", "State = x; return x;")]
    [TestCase("", "System.Console.WriteLine(x); return x;")]
    [TestCase("", "return (int)(object)x;")]
    [TestCase("public static object State = new object();", "return x;")]
    public async Task UnmodeledEffectsAndInitializationRemainUnknown(string members, string body)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source(body, members));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public async Task SourceStaticFieldStoreRefutesPurity()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            Source("State = x; return x;", "public static int State;")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Not.Null);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Single().Region, Is.EqualTo(IrWriteRegion.Static));
    }

    [Test]
    public async Task FieldLocationCapturedAcrossConditionalRemainsUnknown()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public sealed class Cell { public int Value; }
            public static class C {
                [EnforcePure] public static int Target(Cell cell, Cell other) {
                    cell.Value = ((cell = other) == null ? 1 : 2);
                    return 0;
                }
            }
            """));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [TestCase("cell != null", false)]
    [TestCase("cell == null", true)]
    public async Task SourceParameterFieldStoreUsesFaultReachability(string requirement, bool pure)
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public sealed class Cell { public int Value; }
            public static class C {
                [EnforcePure] public static int Target(Cell cell, int x) {
            """ + "Contract.Requires(" + requirement + "); cell.Value = x; return x; } }"));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, pure ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Single().Region, Is.EqualTo(IrWriteRegion.Parameter));
    }

    [Test]
    public async Task NullReceiverStillEvaluatesRightHandSideStaticStore()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public sealed class Cell { public int Value; }
            public static class C {
                public static int State;
                [EnforcePure] public static int Target(Cell cell, int x) {
                    Contract.Requires(cell == null);
                    cell.Value = (State = x);
                    return x;
                }
            }
            """));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Single(write => write.Operation == result.WriteWitness).Region,
            Is.EqualTo(IrWriteRegion.Static));
    }

    [Test]
    public async Task SourceInstanceFieldStoreRefutesPurity()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public sealed class C {
                public int State;
                [EnforcePure] public int Target(int x) { State = x; return x; }
            }
            """));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Single().Region, Is.EqualTo(IrWriteRegion.Field));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FieldStoreCapturesReceiverBeforeRebinding(bool initiallyNull)
    {
        const string source = """
            using SharpProof.Attributes;
            public sealed class Cell { public bool Value; }
            public static class C {
                [EnforcePure] public static int Target(Cell cell, Cell other) {
                    cell.Value = ((cell = other) == null);
                    return 0;
                }
            }
            """;
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var total = preparation.Total!;
        var factory = total.Program.Factory;
        var entries = total.Parameters.Select(parameter => parameter.Entry).ToArray();
        var writes = new List<IrWriteRegion>();
        var execution = new IrProgramInterpreter(factory).Execute(total.Program,
            new Dictionary<IrVarId, IrValue>
            {
                [entries[0]] = initiallyNull ? factory.CreateNullValue(factory.GetVariableInfo(entries[0]).Type) :
                    factory.CreateReferenceValue(factory.GetVariableInfo(entries[0]).Type, new object()),
                [entries[1]] = initiallyNull ? factory.CreateReferenceValue(factory.GetVariableInfo(entries[1]).Type, new object()) :
                    factory.CreateNullValue(factory.GetVariableInfo(entries[1]).Type)
            }, 10000, new IrProgramReplayOptions(_ => null) { WriteObserver = write => writes.Add(write.Region) });
        Assert.That(execution.Status, Is.EqualTo(initiallyNull ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(writes.Contains(IrWriteRegion.Parameter), Is.EqualTo(!initiallyNull));
        Assert.That(writes.Contains(IrWriteRegion.Local), Is.True);
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("FieldWriteRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("FieldWriteRuntime", isCollectible: true);
        try
        {
            var assembly = runtime.LoadFromStream(image);
            var cellType = assembly.GetType("Cell")!;
            var original = initiallyNull ? null : Activator.CreateInstance(cellType);
            var replacement = initiallyNull ? Activator.CreateInstance(cellType) : null;
            var method = assembly.GetType("C")!.GetMethod("Target")!;
            if (initiallyNull)
            {
                var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() =>
                    method.Invoke(null, [original, replacement])));
                Assert.That(thrown!.InnerException, Is.TypeOf<NullReferenceException>());
                Assert.That(cellType.GetField("Value")!.GetValue(replacement), Is.EqualTo(false));
            }
            else
            {
                Assert.That(method.Invoke(null, [original, replacement]), Is.EqualTo(0));
                Assert.That(cellType.GetField("Value")!.GetValue(original), Is.EqualTo(true));
            }
        }
        finally { runtime.Unload(); }
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

    [TestCase("lock (gate) { x++; }")]
    [TestCase("System.Threading.Monitor.Enter(gate);")]
    [TestCase("System.Threading.Monitor.Exit(gate);")]
    [TestCase("while (x > 0) { lock (gate) { x--; } }")]
    public async Task SynchronizationAttemptsRefutePurityWithoutInventingCompletion(string body)
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(SynchronizationSource(body)));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.LockWitness, Is.Not.Null);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrLockInstruction>().Any(synchronization => synchronization.Operation == result.LockWitness), Is.True);
        Assert.That((await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets())).Outcome, Is.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyExceptionsAsync([])).Outcome, Is.Null);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That((await solver.VerifyNormalCompletionAsync()).Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task PreconditionsCanExcludeSynchronizationAndQualifyEveryGoal()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            SynchronizationSource("Contract.Requires(gate == null); if (gate != null) { lock (gate) { x++; } }")));
        Assert.That((await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets())).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure),
            Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyExceptionsAsync([])).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task SourceHelpersPreserveSynchronizationBarriers()
    {
        var source = SynchronizationSource("return Helper(gate, x);").Replace("public static class C {",
            "public static class C { private static int Helper(object gate, int x) { lock (gate) { return x; } }",
            StringComparison.Ordinal);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(
            Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source)), new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.LockWitness, Is.Not.Null);
    }

    [Test]
    public void LockReplayStopsBeforeBodyEffectsAndReturn()
    {
        var source = SynchronizationSource("lock (gate) { new object(); x++; }");
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var total = preparation.Total!;
        var factory = total.Program.Factory;
        var attempts = 0;
        var allocations = 0;
        var execution = new IrProgramInterpreter(factory).Execute(total.Program,
            total.Parameters.ToDictionary(parameter => parameter.Entry, parameter =>
                factory.GetTypeInfo(factory.GetVariableInfo(parameter.Entry).Type).Kind == IrTypeKind.Integer ?
                    factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, 1L) :
                    factory.CreateReferenceValue(factory.GetVariableInfo(parameter.Entry).Type, new object())),
            10000, new IrProgramReplayOptions(_ => null)
            {
                LockObserver = _ => attempts++,
                AllocationObserver = _ => allocations++
            });
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(execution.Instruction, Is.TypeOf<IrLockInstruction>());
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(attempts, Is.EqualTo(1));
        Assert.That(allocations, Is.Zero);
        using var image = new MemoryStream();
        var runtimeSource = source.Replace("Contract.Ensures(Contract.Result<int>() == x); ", "", StringComparison.Ordinal);
        Assert.That(TestCompilation.Create("SynchronizationRuntime", runtimeSource).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("SynchronizationRuntime", isCollectible: true);
        try
        {
            var method = runtime.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
            var execute = method.CreateDelegate<Func<object, int, int>>();
            Assert.That(execute(new object(), 1), Is.EqualTo(2));
            Assert.Throws<ArgumentNullException>(new Action(() => execute(null!, 1)));
        }
        finally { runtime.Unload(); }
    }

    [Test]
    public async Task SourceMonitorLookalikeUsesItsBodyRatherThanFrameworkSemantics()
    {
        var source = SynchronizationSource("System.Threading.Monitor.Enter(gate);") +
            "namespace System.Threading { public static class Monitor { public static void Enter(object value) { } } }";
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrLockInstruction>(), Is.Empty);
    }

    [Test]
    public async Task UnmodeledMonitorOverloadsRemainUnknown()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            SynchronizationSource("System.Threading.Monitor.TryEnter(gate);")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LockRowsRequireReferenceTermsAndCanonicalUnusedSlots(bool unusedSlot)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SynchronizationSource("System.Threading.Monitor.Enter(gate); x = 1;"));
        var graph = artifact.Callables.Single().Total!.Graph;
        var row = graph.Blocks.SelectMany(block => block.Instructions).Single(instruction => instruction.Kind == IrInstructionKind.Lock);
        if (unusedSlot)
        { row.B = 0; }
        else
        { row.A = Array.FindIndex(graph.Terms, term => term.Kind == IrTermKind.Integer); }
        Assert.Throws<System.Text.Json.JsonException>(new Action(() => Prepare(artifact)));
    }

    private static string SynchronizationSource(string body)
    {
        return "using SharpProof.Attributes; public static class C { [EnforcePure, ZeroAllocations] " +
            "public static int Target(object gate, int x) { Contract.Ensures(Contract.Result<int>() == x); " + body + " return x; } }";
    }

    private static string Source(string body, string members = "")
    { return "using SharpProof.Attributes; public static class C { " + members + " [EnforcePure] public static int Target(int x) { " + body + " } }"; }

    private static CompilerCallablePreparation Prepare(CompilerManifestArtifact artifact)
    {
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single();
    }
}
