using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceMemoryReplayTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void BranchSuccessorMustMatchItsObservedBooleanDecision(int mutation)
    {
        if (mutation < 0 || mutation > 2)
        { throw new ArgumentOutOfRangeException(nameof(mutation)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var branch = builder.Branch(entry, factory.CreateOperation(), factory.Boolean(true), yes, no);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var target = factory.CreateVariable("array", array);
        builder.Allocate(yes, factory.CreateOperation(), array, target, factory.Integer(1));
        builder.Return(yes, factory.CreateOperation(), factory.Variable(target));
        builder.Allocate(no, factory.CreateOperation(), array, target, factory.Integer(3));
        builder.Return(no, factory.CreateOperation(), factory.Variable(target));
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null)
        {
            CompletedInstructionObserver = (instruction, value, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                if (ReferenceEquals(instruction, branch))
                {
                    Assert.That(value?.Kind, Is.EqualTo(IrValueKind.Boolean));
                    value = mutation == 0 ? null : mutation == 1 ? factory.CreateBooleanValue(false) : value;
                }
                replay.Observe(instruction, value, approximation);
            }
        };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 100, options, TestContext.CurrentContext.CancellationToken);
        var check = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        if (mutation == 2)
        { Assert.That(check.Bounds, Is.EqualTo(new ResourceMemoryBounds(32, 32, 0, 0, 32, 32))); }
        else
        {
            Assert.That(check.Bounds, Is.Null);
            Assert.That(check.UnknownReason, Is.EqualTo("MemoryReplayTraceIncomplete"));
        }
    }

    [Test]
    public void AForeignInstructionCannotHideBehindTheOwnedFinalReturn()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var returned = builder.Return(builder.CreateBlock(), factory.CreateOperation());
        var program = builder.Build();
        var foreign = new IrProgramBuilder(factory);
        var foreignBlock = foreign.CreateBlock();
        var assigned = foreign.Assign(foreignBlock, factory.CreateOperation(), factory.CreateVariable("foreign", factory.BooleanType), factory.Boolean(true));
        foreign.Return(foreignBlock, factory.CreateOperation());
        foreign.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, TestContext.CurrentContext.CancellationToken);
        var replay = new ResourceMemoryReplay(program);
        replay.Observe(assigned, factory.CreateBooleanValue(true), false);
        replay.Observe(returned, null, false);
        var check = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("MemoryReplayInstructionUnowned"));
    }

    [Test]
    public void RepeatedInstructionExecutionsRequireAnAdmittedRepetitionModel()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var loop = builder.CreateBlock();
        var done = builder.CreateBlock();
        var counter = factory.CreateVariable("counter", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), counter, factory.Integer(0));
        builder.Goto(entry, factory.CreateOperation(), loop);
        builder.Assign(loop, factory.CreateOperation(), counter, factory.Binary(IrBinaryOperator.Add, factory.Variable(counter), factory.Integer(1)));
        builder.Branch(loop, factory.CreateOperation(), factory.Binary(IrBinaryOperator.LessThan, factory.Variable(counter), factory.Integer(2)), loop, done);
        builder.Return(done, factory.CreateOperation());
        var check = Replay(builder.Build(), Layout);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("MemoryReplayInstructionUnowned"));
    }

    [Test]
    public void CancellationCannotPublishPartiallyCollectedLifetimeEvidence()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var returned = builder.Return(builder.CreateBlock(), factory.CreateOperation());
        var program = builder.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, TestContext.CurrentContext.CancellationToken);
        var replay = new ResourceMemoryReplay(program);
        replay.Observe(returned, null, false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => replay.Complete(execution, Layout, cancellation.Token)));
        Assert.Throws<OperationCanceledException>((Action)(() => ResourceMemoryProgram.Evaluate(program, Layout, cancellation.Token)));
    }

    [TestCase(0, 32)]
    [TestCase(8, 32)]
    [TestCase(16, 32)]
    [TestCase(32, 40)]
    [TestCase(64, 48)]
    public void PrimitiveStorageWidthsAgreeWithStaticAndObservedBounds(int width, int bytes)
    {
        if (width is not (0 or 8 or 16 or 32 or 64))
        { throw new ArgumentOutOfRangeException(nameof(width)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var element = width == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(width, signed: false);
        var array = factory.GetOrCreateSequenceType(element);
        var target = factory.CreateVariable("array", array);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Allocate(entry, factory.CreateOperation(), array, target, factory.Integer(3));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(target));
        var program = builder.Build();
        var check = Replay(program, Layout);
        Assert.That(check.Verified, Is.True, check.UnknownReason);
        Assert.That(check.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
        Assert.That(check.Bounds, Is.EqualTo(ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken).Bounds));
        var incompatible = Replay(program, new("CoreCLR", "8.0.32", "arm64", 8));
        Assert.That(incompatible.Bounds, Is.Null);
        Assert.That(incompatible.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void UnsupportedAllocationStorageNeverSuppliesPartialLifetimeNumbers(int kind)
    {
        if (kind < 0 || kind > 3)
        { throw new ArgumentOutOfRangeException(nameof(kind)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (kind < 2)
        {
            var array = factory.GetOrCreateSequenceType(kind == 0 ? factory.ObjectType : factory.IntegerType);
            builder.Allocate(entry, factory.CreateOperation(), array, factory.CreateVariable("array", array), factory.Integer(1),
                kind == 1 ? [factory.Integer(1)] : []);
        }
        else
        {
            var type = kind == 2 ? factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Uncertified") : factory.ObjectType;
            builder.Allocate(entry, factory.CreateOperation(), type, kind == 2 ? factory.CreateVariable("object", type) : null);
        }
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        var observed = Replay(program, Layout);
        Assert.That(observed.Bounds, Is.Null);
        Assert.That(observed.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BorrowedInputCannotBecomeOwnedThroughCopyOrReturn(bool copy)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var input = factory.CreateVariable("input", array);
        var output = factory.CreateVariable("output", array);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (copy)
        { builder.Assign(entry, factory.CreateOperation(), output, factory.Variable(input)); }
        builder.Return(entry, factory.CreateOperation(), factory.Variable(copy ? output : input));
        var program = builder.Build();
        var observed = Replay(program, Layout, new Dictionary<IrVarId, IrValue> { [input] = factory.CreateSequenceValue(array, [factory.CreateIntegerValue(1)]) });
        Assert.That(observed.Bounds, Is.Null);
        Assert.That(observed.UnknownReason, Is.EqualTo("MemoryReplayBorrowedReference"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ObservationFailuresStayUnknownAfterLaterCallbacks(bool approximate)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var returned = builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, TestContext.CurrentContext.CancellationToken);
        var replay = new ResourceMemoryReplay(program);
        if (approximate)
        { replay.Observe(returned, null, true); }
        else
        {
            for (var index = 0; index <= ResourceMemoryTrace.MaximumEvents / 4; index++)
            { replay.Observe(returned, null, false); }
        }
        replay.Observe(returned, null, false);
        var check = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo(approximate ? "MemoryReplayApproximation" : "MemoryReplayBudgetExceeded"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProgramGraphBudgetsRejectOversizedArtifactsBeforeEvidence(bool blocks)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation());
        if (blocks)
        {
            for (var index = 0; index < ResourceMemoryTrace.MaximumEvents / 4; index++)
            { builder.Return(builder.CreateBlock(), factory.CreateOperation()); }
        }
        else
        {
            var unused = builder.CreateBlock();
            var value = factory.CreateVariable("value", factory.BooleanType);
            for (var index = 0; index < ResourceMemoryTrace.MaximumEvents; index++)
            { builder.Assign(unused, factory.CreateOperation(), value, factory.Boolean(false)); }
            builder.Return(unused, factory.CreateOperation());
        }
        var program = builder.Build();
        var check = Replay(program, Layout);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("MemoryReplayBudgetExceeded"));
    }

    [Test]
    public void LegacyExecutionCannotAcquireTotalLifetimeEvidence()
    {
        var factory = new IrFactory(IrExecutionSemantics.Legacy);
        var builder = new IrProgramBuilder(factory);
        var returned = builder.Return(builder.CreateBlock(), factory.CreateOperation());
        var program = builder.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, TestContext.CurrentContext.CancellationToken);
        var replay = new ResourceMemoryReplay(program);
        replay.Observe(returned, null, false);
        var check = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("MemoryReplayRequiresTotalSemantics"));
        Assert.That(ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken).UnknownReason, Is.EqualTo("MemoryProgramRequiresTotalSemantics"));
    }

    private static ResourceMemoryCheck Replay(IrProgram program, ResourceRuntimeLayoutContext layout,
        IReadOnlyDictionary<IrVarId, IrValue>? inputs = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(layout);
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(program.Factory).Execute(program, inputs, 10000, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        return replay.Complete(execution, layout, TestContext.CurrentContext.CancellationToken);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void CompletedWitnessRequiresTheWholeOrderedInstructionPath(int mutation)
    {
        if (mutation < 0 || mutation > 3)
        { throw new ArgumentOutOfRangeException(nameof(mutation)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var node = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Node");
        factory.RegisterClosedClassReferenceType(node, null);
        factory.RegisterObjectStorageType(node, [ResourceStorageKind.Reference]);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, IrFieldSites.Prefix + "child", array, false);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var root = factory.CreateVariable("root", node);
        var child = factory.CreateVariable("child", array);
        var allocation = builder.Allocate(entry, factory.CreateOperation(), node, root);
        builder.Allocate(entry, factory.CreateOperation(), array, child, factory.Integer(1));
        var store = builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field,
            factory.Cast(factory.ObjectType, factory.Variable(root)), field, factory.Variable(child));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(root));
        var program = builder.Build();
        var callbacks = new List<(IrInstruction Instruction, IrValue? Value, bool Approximation)>();
        var options = new IrProgramReplayOptions(static _ => null)
        {
            CompletedInstructionObserver = (instruction, value, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                callbacks.Add((instruction, value, approximation));
            }
        };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        if (mutation == 2)
        { callbacks.Reverse(0, 2); }
        var replay = new ResourceMemoryReplay(program);
        foreach (var callback in callbacks)
        {
            if (mutation == 0 && ReferenceEquals(callback.Instruction, store) ||
                mutation == 1 && ReferenceEquals(callback.Instruction, allocation))
            { continue; }
            replay.Observe(callback.Instruction, callback.Value, callback.Approximation);
        }
        var observed = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        if (mutation == 3)
        {
            Assert.That(observed.Verified, Is.True, observed.UnknownReason);
            Assert.That(observed.Bounds, Is.EqualTo(new ResourceMemoryBounds(56, 56, 0, 0, 56, 56)));
        }
        else
        {
            Assert.That(observed.Bounds, Is.Null, "Dropping a returned-graph store must not manufacture auxiliary bytes.");
            Assert.That(observed.UnknownReason, Is.EqualTo("MemoryReplayTraceIncomplete"));
        }
    }

    [TestCase(false, 0, 32, 32)]
    [TestCase(true, 32, 40, 72)]
    public void CompletedConcreteArraysHaveObservedRetainedAndAuxiliaryCosts(bool temporary, int peak, int retained, int cumulative)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        var alias = factory.CreateVariable("alias", type);
        if (temporary)
        { builder.Allocate(entry, factory.CreateOperation(), type, factory.CreateVariable("dead", type), factory.Integer(1)); }
        builder.Allocate(entry, factory.CreateOperation(), type, array, factory.Integer(temporary ? 3 : 1));
        builder.Assign(entry, factory.CreateOperation(), alias, factory.Variable(array));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(alias));
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        var observed = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(observed.Bounds, Is.EqualTo(new ResourceMemoryBounds(cumulative, cumulative, peak, peak, retained, retained)));
        Assert.That(observed.Bounds, Is.EqualTo(ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken).Bounds));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExceptionalAndTruncatedExecutionsCannotSupplyLifetimeNumbers(bool loop)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (loop)
        { builder.Goto(entry, factory.CreateOperation(), entry); }
        else
        {
            var type = factory.GetOrCreateSequenceType(factory.IntegerType);
            builder.Allocate(entry, factory.CreateOperation(), type, factory.CreateVariable("array", type), factory.Integer(-1));
            builder.Return(entry, factory.CreateOperation());
        }
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(loop ? IrProgramExecutionStatus.StepLimit : IrProgramExecutionStatus.Exception));
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }

    [Test]
    public void ApproximateResultsCannotSupplyLifetimeNumbers()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var value = factory.CreateVariable("value", factory.BooleanType);
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, value);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false)) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 100, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.ConsumedApproximation, Is.True);
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }

    [Test]
    public void UnobservedAndForeignInstructionsCannotBecomeWitnesses()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, TestContext.CurrentContext.CancellationToken);
        Assert.That(new ResourceMemoryReplay(program).Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
        var foreign = new IrProgramBuilder(factory);
        var other = foreign.CreateBlock();
        var returned = foreign.Return(other, factory.CreateOperation());
        var replay = new ResourceMemoryReplay(program);
        replay.Observe(returned, null, false);
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }
}
