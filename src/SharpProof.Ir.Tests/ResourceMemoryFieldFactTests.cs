using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceMemoryFieldFactTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void FieldGuardUpdatesSelectTheOwnedGraphOrRejectUnknownValues(bool boolean, int update)
    {
        if (update < 0 || update > 2)
        { throw new ArgumentOutOfRangeException(nameof(update)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var node = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Node");
        factory.RegisterClosedClassReferenceType(node, null);
        factory.RegisterObjectStorageType(node, [boolean ? ResourceStorageKind.Byte : ResourceStorageKind.Word32]);
        var scalar = boolean ? factory.BooleanType : factory.IntegerType;
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, IrFieldSites.Prefix + "flag", scalar, false);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var root = factory.CreateVariable("root", node);
        var input = factory.CreateVariable("input", scalar);
        var receiver = factory.Cast(factory.ObjectType, factory.Variable(root));
        builder.Allocate(entry, factory.CreateOperation(), node, root);
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, receiver, field,
            boolean ? factory.Boolean(false) : factory.Integer(0));
        if (update != 0)
        {
            builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, receiver, field,
                update == 2 ? factory.Variable(input) : boolean ? factory.Boolean(true) : factory.Integer(1));
        }
        var read = factory.PureOpaque(field, receiver);
        builder.Branch(entry, factory.CreateOperation(), boolean ? read :
            factory.Binary(IrBinaryOperator.Equal, read, factory.Integer(1)), yes, no);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var output = factory.CreateVariable("output", array);
        builder.Allocate(yes, factory.CreateOperation(), array, output, factory.Integer(1));
        builder.Return(yes, factory.CreateOperation(), factory.Variable(output));
        builder.Allocate(no, factory.CreateOperation(), array, output, factory.Integer(3));
        builder.Return(no, factory.CreateOperation(), factory.Variable(output));
        var program = builder.Build();
        var result = ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken);
        if (update == 2)
        {
            Assert.That(result.Bounds, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo("MemoryControlFlowUnsupported"));
            return;
        }
        var retained = update == 1 ? 32 : 40;
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(24 + retained, 24 + retained, 24, 24, retained, retained)));
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.EqualTo(result.Bounds));
    }
    [TestCase("borrowed-receiver")]
    [TestCase("null-receiver")]
    [TestCase("borrowed-child")]
    [TestCase("scalar-expression")]
    public void UnsupportedFieldStoresDiscardPreviouslyCountedAllocations(string shape)
    {
        if (shape is not ("borrowed-receiver" or "null-receiver" or "borrowed-child" or "scalar-expression"))
        { throw new ArgumentOutOfRangeException(nameof(shape)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var root = factory.CreateVariable("root", factory.ObjectType);
        var borrowed = factory.CreateVariable("borrowed", factory.ObjectType);
        builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType, root);
        var scalar = shape == "scalar-expression";
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            IrFieldSites.Prefix + "value", scalar ? factory.IntegerType : factory.ObjectType, false);
        IrTerm receiver = shape == "borrowed-receiver" ? factory.Variable(borrowed) :
            shape == "null-receiver" ? factory.Null(factory.ObjectType) : factory.Variable(root);
        IrTerm value = scalar ? factory.Binary(IrBinaryOperator.Add,
            factory.Variable(factory.CreateVariable("input", factory.IntegerType)), factory.Integer(1)) :
            shape == "borrowed-child" ? factory.Variable(borrowed) : factory.Null(factory.ObjectType);
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, receiver, field, value);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(root));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("MemoryFieldStoreUnsupported"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReturningBorrowedStorageDiscardsPreviouslyCountedAllocations(bool fieldRead)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var root = factory.CreateVariable("root", factory.ObjectType);
        var borrowed = factory.CreateVariable("borrowed", factory.ObjectType);
        builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType, root);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            IrFieldSites.Prefix + "uninitialized", factory.ObjectType, false);
        builder.Return(entry, factory.CreateOperation(), fieldRead ?
            factory.PureOpaque(field, factory.Variable(root)) : factory.Variable(borrowed));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("UnownedMemoryOutput"));
    }
    [TestCase(true, -128, true)]
    [TestCase(true, 127, true)]
    [TestCase(true, 128, false)]
    [TestCase(false, 0, true)]
    [TestCase(false, 255, true)]
    [TestCase(false, 256, false)]
    public void NarrowScalarCastsRequireRepresentableConstants(bool isSigned, int value, bool representable)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var input = factory.CreateVariable("input", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), input, factory.Integer(value));
        builder.Return(entry, factory.CreateOperation(),
            factory.Cast(factory.GetOrCreateIntegerType(8, isSigned), factory.Variable(input)));
        var program = builder.Build();
        var token = TestContext.CurrentContext.CancellationToken;
        var result = ResourceMemoryProgram.Evaluate(program, Layout, token);
        if (!representable)
        {
            Assert.That(result.Bounds, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo("MemoryScalarEffectUnsupported"));
            return;
        }
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(0, 0, 0, 0, 0, 0)));
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, token);
        Assert.That(replay.Complete(execution, Layout, token).Bounds, Is.EqualTo(result.Bounds));
    }
    [TestCase(32, false)]
    [TestCase(32, true)]
    [TestCase(33, false)]
    [TestCase(33, true)]
    public void ScalarTraversalBudgetAdmitsItsBoundaryAndRejectsDeeperTerms(int depth, bool snapshot)
    {
        if (depth is not (32 or 33))
        { throw new ArgumentOutOfRangeException(nameof(depth)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var input = factory.CreateVariable("input", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), input, factory.Integer(0));
        IrTerm value = factory.Variable(input);
        var wide = factory.GetOrCreateIntegerType(64, true);
        for (var index = 0; index < depth; index++)
        { value = factory.Cast(index % 2 == 0 ? wide : factory.IntegerType, value); }
        if (snapshot)
        {
            var stored = factory.CreateVariable("snapshot", value.Type);
            builder.Assign(entry, factory.CreateOperation(), stored, value);
            value = factory.Variable(stored);
        }
        builder.Return(entry, factory.CreateOperation(), value);
        var program = builder.Build();
        var token = TestContext.CurrentContext.CancellationToken;
        var result = ResourceMemoryProgram.Evaluate(program, Layout, token);
        if (depth == 33)
        {
            Assert.That(result.Verified, Is.False);
            Assert.That(result.Bounds, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo("MemoryScalarEffectUnsupported"));
            return;
        }
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(0, 0, 0, 0, 0, 0)));
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, token);
        Assert.That(replay.Complete(execution, Layout, token).Bounds, Is.EqualTo(result.Bounds));
    }
    [TestCase(1023, "MemoryAllocationUnsupported")]
    [TestCase(1024, "MemoryProgramBudgetExceeded")]
    public void InitializerReadsIncludeTheExtentInThePerInstructionBudget(int count, string reason)
    {
        if (count is not (1023 or 1024))
        { throw new ArgumentOutOfRangeException(nameof(count)); }
        ArgumentNullException.ThrowIfNull(reason);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var target = factory.CreateVariable("array", array);
        var initializers = System.Collections.Immutable.ImmutableArray.CreateRange<IrTerm>(
            Enumerable.Repeat(factory.Integer(0), count));
        builder.Allocate(entry, factory.CreateOperation(), array, target, factory.Integer(count), initializers);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(target));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
    }
    [TestCase(32)]
    [TestCase(33)]
    public void NestedBooleanGuardsPreserveTheTraversalBoundary(int depth)
    {
        if (depth is not (32 or 33))
        { throw new ArgumentOutOfRangeException(nameof(depth)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        builder.Assign(entry, factory.CreateOperation(), flag, factory.Boolean(true));
        IrTerm condition = factory.Variable(flag);
        for (var index = 0; index < depth; index++)
        { condition = factory.Unary(IrUnaryOperator.Not, condition); }
        builder.Branch(entry, factory.CreateOperation(), condition, yes, no);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var output = factory.CreateVariable("output", array);
        builder.Allocate(yes, factory.CreateOperation(), array, output, factory.Integer(1));
        builder.Return(yes, factory.CreateOperation(), factory.Variable(output));
        builder.Allocate(no, factory.CreateOperation(), array, output, factory.Integer(3));
        builder.Return(no, factory.CreateOperation(), factory.Variable(output));
        var program = builder.Build();
        var token = TestContext.CurrentContext.CancellationToken;
        var result = ResourceMemoryProgram.Evaluate(program, Layout, token);
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, token);
        var bytes = depth == 32 ? 32 : 40;
        var observed = replay.Complete(execution, Layout, token);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Elements.Length, Is.EqualTo(depth == 32 ? 1 : 3));
        if (depth == 33)
        {
            Assert.That(result.Bounds, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo("MemoryControlFlowUnsupported"));
            Assert.That(observed.Bounds, Is.Null);
            Assert.That(observed.UnknownReason, Is.EqualTo("MemoryReplayValueUnsupported"));
        }
        else
        {
            Assert.That(observed.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
            Assert.That(result.Bounds, Is.EqualTo(observed.Bounds));
        }
    }

    [TestCase(15, null)]
    [TestCase(16, "MemoryScalarEffectUnsupported")]
    [TestCase(17, "MemoryControlFlowUnsupported")]
    public void NestedOwnedFieldReadsKeepTheirGraphWithinBothTraversalBudgets(int depth, string? reason)
    {
        if (depth is < 15 or > 17)
        { throw new ArgumentOutOfRangeException(nameof(depth)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var node = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Node");
        factory.RegisterClosedClassReferenceType(node, null);
        factory.RegisterObjectStorageType(node, [ResourceStorageKind.Reference]);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, IrFieldSites.Prefix + "next", node, false);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var root = factory.CreateVariable("root", node);
        builder.Allocate(entry, factory.CreateOperation(), node, root);
        var current = root;
        IrTerm read = factory.Variable(root);
        for (var index = 0; index < depth; index++)
        {
            var child = factory.CreateVariable("child", node);
            builder.Allocate(entry, factory.CreateOperation(), node, child);
            builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field,
                factory.Cast(factory.ObjectType, factory.Variable(current)), field, factory.Variable(child));
            read = factory.PureOpaque(field, factory.Cast(factory.ObjectType, read));
            current = child;
        }
        builder.Branch(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.Equal, read, factory.Null(node)), yes, no);
        builder.Return(yes, factory.CreateOperation(), factory.Null(node));
        builder.Return(no, factory.CreateOperation(), factory.Variable(root));
        var program = builder.Build();
        var token = TestContext.CurrentContext.CancellationToken;
        var result = ResourceMemoryProgram.Evaluate(program, Layout, token);
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, token);
        var observed = replay.Complete(execution, Layout, token);
        var bytes = 24 * (depth + 1);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Reference, Is.SameAs(execution.GetCurrentValue(root)!.Reference));
        var identities = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var actual = execution.ReturnValue!;
        for (var index = 0; index <= depth; index++)
        {
            Assert.That(actual.Kind, Is.EqualTo(IrValueKind.Reference));
            Assert.That(identities.Add(actual.Reference), Is.True);
            if (index != depth)
            {
                Assert.That(execution.Heap!.TryReadField(factory, actual, field, out var next), Is.True);
                actual = next;
            }
        }
        Assert.That(24 * identities.Count, Is.EqualTo(bytes));
        Assert.That(actual.Reference, Is.SameAs(execution.GetCurrentValue(current)!.Reference));
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        if (reason is null)
        {
            Assert.That(observed.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
            Assert.That(result.Bounds, Is.EqualTo(observed.Bounds));
        }
        else
        {
            Assert.That(result.Bounds, Is.Null);
            Assert.That(observed.Bounds, Is.Null);
            Assert.That(observed.UnknownReason, Is.EqualTo("MemoryReplayValueUnsupported"));
        }
    }
}
