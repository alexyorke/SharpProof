using System.Collections.Immutable;
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
        System.ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(Source(body)));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Null);
    }

    [Test]
    public async Task ExplicitDelegateConstructionDoesNotExecuteItsImpureTarget()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(Source(
            "System.Action action = new System.Action(Sink); return x;",
            "private static void Sink() { System.Console.WriteLine(1); }")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrAllocationInstruction>().Single().Target, Is.Not.Null);
    }

    [Test]
    public async Task DelegateConstructionDoesNotRunTargetTypeInitialization()
    {
        var source = Source("System.Action action = new System.Action(Other.Sink); return x;",
            "public static int State; private static class Other { static Other() { State = 1; } public static void Sink() { State = 2; } }");
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrWriteInstruction>(), Is.Not.Empty);
        Assert.That(preparation.Total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrAllocationInstruction>(), Is.Not.Empty);
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
        var row = artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Write));
        {
            var changedFixture = artifact.Callables.Single().Total!.Graph.ChangeRow(row, row with { A = (int)region });
            row = changedFixture.Item1;
            artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { Graph = changedFixture.Item2 } }) };
        }
        var preparation = Prepare(artifact);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, pure ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness.HasValue, Is.EqualTo(!pure));
        if (!pure)
        {
            Assert.That((int)result.EntryModel.Values.Single().IntegerNumericValue, Is.Zero);
            Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrWriteInstruction>()
                .Any(write => write is null ? throw new System.ArgumentNullException(nameof(write)) : (write.Operation == result.WriteWitness && write.Region == region)), Is.True);
        }
    }

    [Test]
    public async Task PreconditionsCanExcludeNonlocalWriteEvents()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source("Contract.Requires(x != 0); if (x == 0) x = 1; return x;"));
        {
            artifact = artifact with
            {
                Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with
                {
                    Total = artifact.Callables.Single().Total! with
                    {
                        Graph = artifact.Callables.Single().Total!.Graph.ReplaceRow(artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Write)), artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Write)) with
            { A = (int)IrWriteRegion.Field })
                    }
                })
            };
        }
        Assert.That((await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome,
            Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task LoopWriteWitnessRequiresOriginalBodyReplay()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source("while (x > 0) x--; return x;"));
        {
            artifact = artifact with
            {
                Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with
                {
                    Total = artifact.Callables.Single().Total! with
                    {
                        Graph = artifact.Callables.Single().Total!.Graph.ReplaceRow(artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Write)), artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Write)) with
            { A = (int)IrWriteRegion.Element })
                    }
                })
            };
        }
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        var row = artifact.Callables.Single().Total!.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Write));
        {
            var changedFixture = artifact.Callables.Single().Total!.Graph.ChangeRow(row, row with { A = region });
            row = changedFixture.Item1;
            artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { Graph = changedFixture.Item2 } }) };
        }
        if (unusedSlot)
        {
            {
                var changedFixture = artifact.Callables.Single().Total!.Graph.ChangeRow(row, row with { B = 0 });
                row = changedFixture.Item1;
                artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { Graph = changedFixture.Item2 } }) };
            }
        }
        Assert.Throws<System.Text.Json.JsonException>(new Action(() => Prepare(artifact)));
    }

    [TestCase("public static volatile int State;", "State = x; return x;")]
    [TestCase("public static int State = 1;", "State = x; return x;")]
    [TestCase("", "return (int)(object)x;")]
    [TestCase("public static object State = new object();", "return x;")]
    public async Task UnmodeledEffectsAndInitializationRemainUnknown(string members, string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(members);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source(body, members));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    // Writing the elements of an array the body created and keeps is not
    // observable; writing one that escapes, or a parameter's, is.
    [TestCase("var buffer = new int[1]; buffer[0] = x; return buffer[0];", true)]
    [TestCase("var buffer = new int[2]; buffer[1] += x; return buffer.Length;", true)]
    [TestCase("var buffer = new int[1]; buffer[0] = x; Keep = buffer; return x;", false)]
    public async Task FreshArrayWritesArePure(string body, bool pure)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            Source(body, "public static int[]? Keep;")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (pure)
        { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        else
        { Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>()); }
    }

    // Writing a field of an object the body created is not observable.
    [TestCase("new Cell().State++; return x;")]
    [TestCase("var cell = new Cell(); cell.State = x; cell.State += 1; return cell.State;")]
    public async Task FreshObjectWritesArePure(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            Source(body, "public sealed class Cell { public int State; }"))), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    // A static field of a type without an initializer is read without running code.
    [TestCase("State = x; return x;")]
    [TestCase("State = x; return State;")]
    public async Task SourceStaticFieldStoreRefutesPurity(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            Source(body, "public static int State;")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Not.Null);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
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
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [TestCase("cell != null", false)]
    [TestCase("cell == null", true)]
    public async Task SourceParameterFieldStoreUsesFaultReachability(string requirement, bool pure)
    {
        System.ArgumentNullException.ThrowIfNull(requirement);
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public sealed class Cell { public int Value; }
            public static class C {
                [EnforcePure] public static int Target(Cell cell, int x) {
            """ + "Contract.Requires(" + requirement + "); cell.Value = x; return x; } }"));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, pure ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
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
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrWriteInstruction>().Single(write => write is null ? throw new System.ArgumentNullException(nameof(write)) : (write.Operation == result.WriteWitness)).Region,
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
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
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
        var entries = total.Parameters.Select(parameter => parameter.Entry).ToImmutableArray();
        var writesBuilder = ImmutableArray.CreateBuilder<IrWriteRegion>();
        var execution = new IrProgramInterpreter(factory).Execute(total.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entries[0], initiallyNull ? factory.CreateNullValue(factory.GetVariableInfo(entries[0]).Type) :
                    factory.CreateReferenceValue(factory.GetVariableInfo(entries[0]).Type, new object())).SetItem(entries[1], initiallyNull ? factory.CreateReferenceValue(factory.GetVariableInfo(entries[1]).Type, new object()) :
                    factory.CreateNullValue(factory.GetVariableInfo(entries[1]).Type)), 10000, new IrProgramReplayOptions(_ => null) { WriteObserver = write => { System.ArgumentNullException.ThrowIfNull(write); writesBuilder.Add(write.Region); } });
        var writes = writesBuilder.ToImmutable();
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
        { { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { EffectsCompleteAtEntry = false } }) }; } }
        else
        { { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { ValidEffectClaimIds = [] } }) }; } }
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(artifact), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(initialization ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.UnsupportedContract));
    }

    [TestCase("lock (gate) { x++; }")]
    [TestCase("System.Threading.Monitor.Enter(gate);")]
    [TestCase("System.Threading.Monitor.Exit(gate);")]
    [TestCase("while (x > 0) { lock (gate) { x--; } }")]
    public async Task SynchronizationAttemptsRefutePurityWithoutInventingCompletion(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(SynchronizationSource(body)));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.LockWitness, Is.Not.Null);
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrLockInstruction>().Any(synchronization => synchronization is null ? throw new System.ArgumentNullException(nameof(synchronization)) : (synchronization.Operation == result.LockWitness)), Is.True);
        Assert.That((await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyExceptionsAsync([], cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.Null);
        Assert.That((await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.Not.TypeOf<ProvenOutcome>());
        Assert.That((await solver.VerifyNormalCompletionAsync(cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task PreconditionsCanExcludeSynchronizationAndQualifyEveryGoal()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            SynchronizationSource("Contract.Requires(gate == null); if (gate != null) { lock (gate) { x++; } }")));
        Assert.That((await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken),
            Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyExceptionsAsync([], cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That((await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task SourceHelpersPreserveSynchronizationBarriers()
    {
        var source = SynchronizationSource("return Helper(gate, x);").Replace("public static class C {",
            "public static class C { private static int Helper(object gate, int x) { lock (gate) { return x; } }",
            StringComparison.Ordinal);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(
            Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source)), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
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
            ImmutableDictionaryCapture.Create(total.Parameters, parameter => parameter.Entry, parameter =>
                ((parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(parameter)) : (factory.GetTypeInfo(factory.GetVariableInfo(parameter.Entry).Type).Kind == IrTypeKind.Integer ?
                    factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, 1L) :
                    factory.CreateReferenceValue(factory.GetVariableInfo(parameter.Entry).Type, new object())))),
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
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrLockInstruction>(), Is.Empty);
    }

    [Test]
    public async Task UnmodeledMonitorOverloadsRemainUnknown()
    {
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(
            SynchronizationSource("System.Threading.Monitor.TryEnter(gate);")));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LockRowsRequireReferenceTermsAndCanonicalUnusedSlots(bool unusedSlot)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SynchronizationSource("System.Threading.Monitor.Enter(gate); x = 1;"));
        var graph = artifact.Callables.Single().Total!.Graph;
        var row = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Lock));
        if (unusedSlot)
        {
            (row, graph) = graph.ChangeRow(row, row with { B = 0 });
            { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { Graph = graph } }) }; }
        }
        else
        {
            (row, graph) = graph.ChangeRow(row, row with { A = PortableGraphChanges.FindIndex(graph.Terms, term => term is null ? throw new System.ArgumentNullException(nameof(term)) : (term.Kind == IrTermKind.Integer)) });
            { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { Graph = graph } }) }; }
        }
        Assert.Throws<System.Text.Json.JsonException>(new Action(() => Prepare(artifact)));
    }

    [TestCase("State++;", 1)]
    [TestCase("++State;", 1)]
    [TestCase("State--;", -1)]
    [TestCase("--State;", -1)]
    [TestCase("_ = State++;", 1)]
    [TestCase("_ = ++State;", 1)]
    [TestCase("_ = State--;", -1)]
    [TestCase("_ = --State;", -1)]
    [TestCase("_ = (long)State++;", 1)]
    [TestCase("_ = (long)++State;", 1)]
    [TestCase("_ = unchecked((byte)State++);", 1)]
    [TestCase("_ = (ulong)(short)State++;", 1)]
    public async Task DiscardedUncheckedStaticIntMutationRefutesPurity(string body, int expectedState)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var source = Source(body + " return x;", "public static int State;");
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("StaticMutationOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("StaticMutationOracle", isCollectible: true);
        try
        {
            var type = runtime.LoadFromStream(image).GetType("C")!;
            var run = type.GetMethod("Target")!.CreateDelegate<Func<int, int>>();
            Assert.That(run(7), Is.EqualTo(7));
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(expectedState));
        }
        finally { runtime.Unload(); }
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        Assert.That(preparation.Total, Is.Not.Null);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        var writes = preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrWriteInstruction>().ToImmutableArray();
        Assert.That(writes, Has.Length.EqualTo(1));
        Assert.That(writes[0].Region, Is.EqualTo(IrWriteRegion.Static));
        Assert.That(result.WriteWitness, Is.EqualTo(writes[0].Operation));
        var factory = preparation.Total.Program.Factory;
        var input = preparation.Total.Parameters.Single().Entry;
        var observed = 0;
        var execution = new IrProgramInterpreter(factory).Execute(preparation.Total.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(input, factory.CreateIntegerValue(factory.GetVariableInfo(input).Type, 7L)),
            maximumSteps: 10000, replayOptions: new IrProgramReplayOptions(static _ => null)
            {
                WritePrefixObserver = (write, approximate) =>
                {
                    System.ArgumentNullException.ThrowIfNull(write);
                    Assert.That(approximate, Is.False);
                    Assert.That(write.Operation, Is.EqualTo(result.WriteWitness));
                    observed++;
                }
            });
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(7)));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(observed, Is.EqualTo(1));
    }

    [TestCase("public static int State;", "return State++;")]
    [TestCase("public static int State;", "checked { State++; } return x;")]
    [TestCase("public static int State;", "State += x; return x;")]
    [TestCase("public static volatile int State;", "State++; return x;")]
    [TestCase("public static int State; static C() { State = 1; }", "State++; return x;")]
    [TestCase("public static int State = 1;", "State++; return x;")]
    [TestCase("public static byte State;", "State++; return x;")]
    [TestCase("public static int State;", "int _ = 0; _ = State++; return x;")]
    [TestCase("public static int State;", "long _ = 0; _ = (long)State++; return x;")]
    [TestCase("public static int State;", "_ = checked((byte)unchecked(State++)); return x;")]
    [TestCase("public static int State;", "_ = (object)State++; return x;")]
    [TestCase("public static int State; public struct Wrapper { public static implicit operator Wrapper(int value) => default; }", "_ = (Wrapper)State++; return x;")]
    [TestCase("public static int State;", "int ignored = State++; return x;")]
    [TestCase("public static int State;", "return State++ + x;")]
    public async Task UnsupportedStaticFieldMutationSlicesRemainUnknown(string members, string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(members);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(Source(body, members))), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }


    [TestCase("cell.Value++;", false, IrWriteRegion.Parameter, 6, 10, 7)]
    [TestCase("++cell.Value;", false, IrWriteRegion.Parameter, 6, 10, 7)]
    [TestCase("cell.Value--;", false, IrWriteRegion.Parameter, 4, 10, 7)]
    [TestCase("--cell.Value;", false, IrWriteRegion.Parameter, 4, 10, 7)]
    [TestCase("_ = cell.Value++;", false, IrWriteRegion.Parameter, 6, 10, 7)]
    [TestCase("_ = ++cell.Value;", false, IrWriteRegion.Parameter, 6, 10, 7)]
    [TestCase("_ = cell.Value--;", false, IrWriteRegion.Parameter, 4, 10, 7)]
    [TestCase("_ = --cell.Value;", false, IrWriteRegion.Parameter, 4, 10, 7)]
    [TestCase("cell.Value++;", true, IrWriteRegion.Parameter, 5, 10, 7)]
    [TestCase("_ = --cell.Value;", true, IrWriteRegion.Parameter, 5, 10, 7)]
    [TestCase("var local = cell; local.Value++;", false, IrWriteRegion.Field, 6, 10, 7)]
    [TestCase("(cell = other).Value++;", false, IrWriteRegion.Field, 5, 11, 7)]
    [TestCase("(cell = other).Value--;", true, IrWriteRegion.Field, 5, 9, 7)]
    [TestCase("(x++ > 0 ? cell : other).Value++;", false, IrWriteRegion.Field, 6, 10, 8)]
    public async Task DiscardedInstanceMutationPreservesReceiverFaultAndWrite(
        string body, bool nullReceiver, IrWriteRegion region, int final, int otherFinal, int returned)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var rebind = body?.StartsWith("(cell = other)", StringComparison.Ordinal) == true;
        var source = InstanceMutationSource("Contract.Requires(cell " + (nullReceiver ? "==" : "!=") + " null); Contract.Requires(other != null); " + body + " return x;");
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("InstanceMutationPrototype", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("InstanceMutationPrototype", isCollectible: true);
        try
        {
            var assembly = runtime.LoadFromStream(image);
            var type = assembly.GetType("C")!;
            var cellType = assembly.GetType("Cell")!;
            var cell = Activator.CreateInstance(cellType)!;
            var other = Activator.CreateInstance(cellType)!;
            cellType.GetField("Value")!.SetValue(cell, 5);
            cellType.GetField("Value")!.SetValue(other, 10);
            var method = type.GetMethod("Target")!;
            if (nullReceiver && !rebind)
            {
                var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => method.Invoke(null, [null, other, 7])));
                Assert.That(thrown!.InnerException, Is.TypeOf<NullReferenceException>());
            }
            else
            { Assert.That(method.Invoke(null, [nullReceiver ? null : cell, other, 7]), Is.EqualTo(returned)); }
            Assert.That(cellType.GetField("Value")!.GetValue(cell), Is.EqualTo(final));
            Assert.That(cellType.GetField("Value")!.GetValue(other), Is.EqualTo(otherFinal));
        }
        finally { runtime.Unload(); }
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var faults = nullReceiver && !rebind;
        Assert.That(result.Outcome, faults ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        var total = preparation.Total!;
        var write = total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrWriteInstruction>().Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Region != IrWriteRegion.Local));
        Assert.That(write.Region, Is.EqualTo(region));
        Assert.That(result.WriteWitness, faults ? Is.Null : Is.EqualTo(write.Operation));
        var factory = total.Program.Factory;
        var entries = total.Parameters.Select(parameter => parameter.Entry).ToImmutableArray();
        var observed = 0;
        // The objects enter with the field values the runtime run used.
        var field = write.Field!.Value;
        var fieldType = factory.GetMemberInfo(field).ReturnType;
        var replay = new IrProgramInterpreter(factory).Execute(total.Program, global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entries[0], nullReceiver ? factory.CreateNullValue(factory.GetVariableInfo(entries[0]).Type) : factory.CreateReferenceValue(
                factory.GetVariableInfo(entries[0]).Type, new IrObjectState().WithField(field, factory.CreateIntegerValue(fieldType, 5L)))).SetItem(entries[1], factory.CreateReferenceValue(factory.GetVariableInfo(entries[1]).Type,
                new IrObjectState().WithField(field, factory.CreateIntegerValue(fieldType, 10L)))).SetItem(entries[2], factory.CreateIntegerValue(factory.GetVariableInfo(entries[2]).Type, 7L)), 10000, new IrProgramReplayOptions(static _ => null)
                {
                    WritePrefixObserver = (item, approximate) =>
                    {
                        System.ArgumentNullException.ThrowIfNull(item);
                        Assert.That(approximate, Is.False);
                        if (item.Region != IrWriteRegion.Local)
                        { Assert.That(item.Operation, Is.EqualTo(write.Operation)); observed++; }
                    }
                });
        Assert.That(replay.Status, Is.EqualTo(faults ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(observed, Is.EqualTo(faults ? 0 : 1));
        if (!faults)
        { Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(returned))); }
    }

    // A mutation whose value or overflow depends on the field reads the value
    // its object enters with, so the reached write replays concretely.
    [TestCase("cell.Value += x;", "public int Value;")]
    [TestCase("checked { cell.Value++; }", "public int Value;")]
    [TestCase("return cell.Value++;", "public int Value;")]
    [TestCase("int ignored = cell.Value++;", "public int Value;")]
    [TestCase("int _ = 0; _ = cell.Value++;", "public int Value;")]
    [TestCase("_ = (long)cell.Value++;", "public int Value;")]
    [TestCase("cell.Value++;", "public byte Value;")]
    public async Task ValueObservingMutationsAreRefuted(string body, string field)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(field);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(InstanceMutationSource(body + " return x;", field))), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.WriteWitness, Is.Not.Null);
    }

    [TestCase("cell.Value++;", "public volatile int Value;")]
    [TestCase("cell.Value++;", "public int Value; static Cell() { }")]
    public async Task OtherInstanceMutationShapesRemainClosed(string body, string field)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(field);
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(InstanceMutationSource(body + " return x;", field))), new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public async Task ImplicitThisMutationUsesOwnedFieldRegion()
    {
        const string source = "using SharpProof.Attributes; public sealed class C { public int Value; [EnforcePure] public int Target(int x) { Value++; return x; } }";
        var preparation = Prepare(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrWriteInstruction>().Single().Region, Is.EqualTo(IrWriteRegion.Field));
    }

    private static string InstanceMutationSource(string body, string field = "public int Value;")
    { System.ArgumentNullException.ThrowIfNull(body, nameof(body)); System.ArgumentNullException.ThrowIfNull(field, nameof(field)); return "using SharpProof.Attributes; public sealed class Cell { " + field + " } public static class C { [EnforcePure] public static int Target(Cell cell, Cell other, int x) { " + body + " } }"; }


    private static string SynchronizationSource(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        return "using SharpProof.Attributes; public static class C { [EnforcePure, ZeroAllocations] " +
            "public static int Target(object gate, int x) { Contract.Ensures(Contract.Result<int>() == x); " + body + " return x; } }";
    }

    private static string Source(string body, string members = "")
    { System.ArgumentNullException.ThrowIfNull(body, nameof(body)); System.ArgumentNullException.ThrowIfNull(members, nameof(members)); return "using SharpProof.Attributes; public static class C { " + members + " [EnforcePure] public static int Target(int x) { " + body + " } }"; }

    private static CompilerCallablePreparation Prepare(CompilerManifestArtifact artifact)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single();
    }
}
