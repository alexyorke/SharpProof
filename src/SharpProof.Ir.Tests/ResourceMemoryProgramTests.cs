using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceMemoryProgramTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    public void StaticDelegateLifetimesRequireBothCompilerBindingAndTypeEvidence(bool certified, int marker)
    {
        if (marker < 0 || marker > 2)
        { throw new ArgumentOutOfRangeException(nameof(marker)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Callback");
        if (certified)
        { factory.RegisterDelegateReferenceType(type); }
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var target = factory.CreateVariable("callback", type);
        var site = marker == 0 ? factory.CreateOperation() : factory.CreateOperation(
            marker == 1 ? IrStaticDelegateAllocationSite.Description : IrStaticDelegateAllocationSite.Description + "Suffix");
        builder.Allocate(entry, site, type, target);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(target));
        var program = builder.Build();
        var check = ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken);
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 100, options, TestContext.CurrentContext.CancellationToken);
        var observed = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        if (certified && marker == 1)
        {
            Assert.That(check.Bounds, Is.EqualTo(new ResourceMemoryBounds(64, 64, 0, 0, 64, 64)), check.UnknownReason);
            Assert.That(observed.Bounds, Is.EqualTo(check.Bounds), observed.UnknownReason);
        }
        else
        {
            Assert.That(check.Bounds, Is.Null);
            Assert.That(check.UnknownReason, Is.EqualTo("MemoryObjectStorageUnavailable"));
            Assert.That(observed.Bounds, Is.Null);
            Assert.That(observed.UnknownReason, Is.EqualTo("MemoryReplayObjectStorageUnavailable"));
        }
    }

    [Test]
    public void StaticDelegateAdmissionRequiresItsFactoryAndInstruction()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var allocation = builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType);
        Assert.Throws<ArgumentNullException>((Action)(() => IrStaticDelegateAllocationSite.Matches(null!, allocation)));
        Assert.Throws<ArgumentNullException>((Action)(() => IrStaticDelegateAllocationSite.Matches(factory, null!)));
    }

    [TestCase(false, 40)]
    [TestCase(true, 32)]
    public void UnsignedGuardConstantsDoNotPassThroughASignedIntermediary(bool maximum, int retained)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.GetOrCreateIntegerType(64, signed: false);
        var value = factory.CreateVariable("value", integer);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(integer, maximum ? ulong.MaxValue : 0UL));
        builder.Branch(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.GreaterThan,
            factory.Variable(value), factory.Cast(integer, factory.Integer(0))), yes, no);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var result = factory.CreateVariable("result", array);
        builder.Allocate(yes, factory.CreateOperation(), array, result, factory.Integer(1));
        builder.Return(yes, factory.CreateOperation(), factory.Variable(result));
        builder.Allocate(no, factory.CreateOperation(), array, result, factory.Integer(3));
        builder.Return(no, factory.CreateOperation(), factory.Variable(result));
        var program = builder.Build();
        var check = ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Verified, Is.True, check.UnknownReason);
        Assert.That(check.Bounds, Is.EqualTo(new ResourceMemoryBounds(retained, retained, 0, 0, retained, retained)));
    }

    [Test]
    public void TruncatingIntegerConversionsDoNotBecomeValuePreservingConstants()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var small = factory.GetOrCreateIntegerType(8, signed: false);
        var wide = factory.CreateVariable("wide", factory.IntegerType);
        var count = factory.CreateVariable("count", small);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), wide, factory.Integer(256));
        builder.Assign(entry, factory.CreateOperation(), count, factory.Cast(small, factory.Variable(wide)));
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var target = factory.CreateVariable("array", array);
        builder.Allocate(entry, factory.CreateOperation(), array, target, factory.Cast(factory.IntegerType, factory.Variable(count)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(target));
        var check = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(0, 0)]
    [TestCase(1, 24)]
    [TestCase(3, 32)]
    [TestCase(6, 40)]
    public void StringLifetimesPreserveAliasesAndSharedEmptyStorage(int length, int bytes)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var text = factory.CreateVariable("text", factory.StringType);
        var saved = factory.CreateVariable("saved", factory.ObjectType);
        var empty = factory.CreateVariable("empty", factory.StringType);
        var fill = factory.Integer(factory.GetOrCreateIntegerType(16, signed: false), 'x');
        builder.Allocate(entry, factory.CreateOperation(), factory.StringType, text, factory.Integer(length), [fill]);
        builder.Assign(entry, factory.CreateOperation(), saved, factory.Cast(factory.ObjectType, factory.Variable(text)));
        builder.Allocate(entry, factory.CreateOperation(), factory.StringType, empty, factory.Integer(0), [fill]);
        builder.Assign(entry, factory.CreateOperation(), text, factory.String(string.Empty));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(saved));
        var program = builder.Build();
        var result = ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, TestContext.CurrentContext.CancellationToken);
        var observed = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(observed.Verified, Is.True, observed.UnknownReason);
        Assert.That(observed.Bounds, Is.EqualTo(result.Bounds));
    }

    [Test]
    public void StringContentEqualityCannotBeProvedFromDistinctAllocationIdentities()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var first = factory.CreateVariable("first", factory.StringType);
        var second = factory.CreateVariable("second", factory.StringType);
        var fill = factory.Integer(factory.GetOrCreateIntegerType(16, signed: false), 'x');
        builder.Allocate(entry, factory.CreateOperation(), factory.StringType, first, factory.Integer(1), [fill]);
        builder.Allocate(entry, factory.CreateOperation(), factory.StringType, second, factory.Integer(1), [fill]);
        builder.Branch(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.Equal, factory.Variable(first), factory.Variable(second)), yes, no);
        builder.Return(yes, factory.CreateOperation(), factory.Variable(first));
        builder.Return(no, factory.CreateOperation(), factory.String(string.Empty));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("MemoryControlFlowUnsupported"));
    }

    [TestCase(0, 32, 0)]
    [TestCase(1, 40, 48)]
    public void EmptyIdentitySharingAndFreshStringReferenceGuardsKeepReadRootsLive(int length, int retained, int auxiliary)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var first = factory.CreateVariable("first", factory.StringType);
        var second = factory.CreateVariable("second", factory.StringType);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var comparison = factory.CreateVariable("comparison", factory.BooleanType);
        var result = factory.CreateVariable("result", array);
        var fill = factory.Integer(factory.GetOrCreateIntegerType(16, signed: false), 'x');
        builder.Allocate(entry, factory.CreateOperation(), factory.StringType, first, factory.Integer(length), [fill]);
        builder.Allocate(entry, factory.CreateOperation(), factory.StringType, second, factory.Integer(length), [fill]);
        builder.Assign(entry, factory.CreateOperation(), comparison, factory.Binary(IrBinaryOperator.Equal,
            factory.Cast(factory.ObjectType, factory.Variable(first)), factory.Cast(factory.ObjectType, factory.Variable(second))));
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(comparison), yes, no);
        builder.Allocate(yes, factory.CreateOperation(), array, result, factory.Integer(1));
        builder.Return(yes, factory.CreateOperation(), factory.Variable(result));
        builder.Allocate(no, factory.CreateOperation(), array, result, factory.Integer(3));
        builder.Return(no, factory.CreateOperation(), factory.Variable(result));
        var program = builder.Build();
        var bounds = ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(bounds.Verified, Is.True, bounds.UnknownReason);
        Assert.That(bounds.Bounds!.Value.RetainedUpper, Is.EqualTo(new System.Numerics.BigInteger(retained)));
        Assert.That(bounds.Bounds.Value.PeakAuxiliaryUpper, Is.EqualTo(new System.Numerics.BigInteger(auxiliary)));
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, TestContext.CurrentContext.CancellationToken);
        var observed = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(observed.Verified, Is.True, observed.UnknownReason);
        Assert.That(observed.Bounds, Is.EqualTo(bounds.Bounds));
    }

    [TestCase(0, 24)]
    [TestCase(1, 32)]
    [TestCase(3, 40)]
    public void TypedArrayReturnDerivesRetainedStorageWithoutProducerSizes(int length, int bytes)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        builder.Allocate(entry, factory.CreateOperation(), type, array, factory.Integer(length));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(array));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
    }

    [TestCase(false, 40)]
    [TestCase(true, 72)]
    public void LastUseRootsDistinguishDeadStorageFromLiveAliases(bool preserveAlias, int peak)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var first = factory.CreateVariable("first", type);
        var second = factory.CreateVariable("second", type);
        var alias = factory.CreateVariable("alias", type);
        builder.Allocate(entry, factory.CreateOperation(), type, first, factory.Integer(1));
        builder.Allocate(entry, factory.CreateOperation(), type, second, factory.Integer(3));
        if (preserveAlias)
        { builder.Assign(entry, factory.CreateOperation(), alias, factory.Variable(first)); }
        builder.Return(entry, factory.CreateOperation());
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(72, 72, peak, peak, 0, 0)));
    }

    [Test]
    public void ReturnedAliasOwnsTheOriginalAllocationAcrossBlocks()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.BooleanType);
        var array = factory.CreateVariable("array", type);
        var alias = factory.CreateVariable("alias", type);
        builder.Allocate(entry, factory.CreateOperation(), type, array, factory.Integer(9));
        builder.Assign(entry, factory.CreateOperation(), alias, factory.Variable(array));
        builder.Goto(entry, factory.CreateOperation(), exit);
        builder.Return(exit, factory.CreateOperation(), factory.Variable(alias));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(40, 40, 0, 0, 40, 40)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void UnsupportedEffectsAndUnownedReferencesReturnUnknown(int kind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("input", type);
        switch (kind)
        {
            case 0:
                builder.Return(entry, factory.CreateOperation(), factory.Variable(array));
                break;
            case 1:
                builder.Goto(entry, factory.CreateOperation(), entry);
                break;
            case 2:
                builder.Assume(entry, factory.CreateOperation(), factory.Boolean(false));
                builder.Return(entry, factory.CreateOperation());
                break;
            case 5:
                builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType);
                builder.Return(entry, factory.CreateOperation());
                break;
            default:
                builder.Allocate(entry, factory.CreateOperation(), type, array,
                    kind == 3 ? factory.Integer(-1) : factory.Variable(factory.CreateVariable("n", factory.IntegerType)));
                builder.Return(entry, factory.CreateOperation());
                break;
        }
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(0, -1, 0, false)]
    [TestCase(1, -1, 0, true)]
    [TestCase(2, -1, 0, true)]
    [TestCase(3, 0, 0, true)]
    [TestCase(4, int.MinValue, int.MaxValue, false)]
    [TestCase(5, int.MaxValue, int.MinValue, true)]
    public void OnlyProvenConstantGuardEdgesContributeStorage(int operation, int left, int right, bool taken)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        if (operation < 0 || operation > 5)
        { throw new ArgumentOutOfRangeException(nameof(operation)); }
        var operators = new[] { IrBinaryOperator.Equal, IrBinaryOperator.NotEqual, IrBinaryOperator.LessThan,
            IrBinaryOperator.LessThanOrEqual, IrBinaryOperator.GreaterThan, IrBinaryOperator.GreaterThanOrEqual };
        builder.Branch(entry, factory.CreateOperation(), factory.Binary(operators[operation], factory.Integer(left), factory.Integer(right)), yes, no);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        builder.Allocate(yes, factory.CreateOperation(), type, array, factory.Integer(1));
        builder.Return(yes, factory.CreateOperation(), factory.Variable(array));
        builder.Allocate(no, factory.CreateOperation(), type, array, factory.Integer(3));
        builder.Return(no, factory.CreateOperation(), factory.Variable(array));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        var bytes = taken ? 32 : 40;
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
    }

    [TestCase(false, 32)]
    [TestCase(true, 40)]
    public void CopiedConstantsAndReassignmentsDetermineGuards(bool overwrite, int bytes)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var dimension = factory.CreateVariable("dimension", factory.IntegerType);
        var copy = factory.CreateVariable("copy", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), dimension, factory.Integer(1));
        if (overwrite)
        { builder.Assign(entry, factory.CreateOperation(), dimension, factory.Integer(3)); }
        builder.Assign(entry, factory.CreateOperation(), copy, factory.Variable(dimension));
        builder.Branch(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.LessThan, factory.Variable(copy), factory.Integer(2)), yes, no);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        builder.Allocate(yes, factory.CreateOperation(), type, array, factory.Variable(copy));
        builder.Return(yes, factory.CreateOperation(), factory.Variable(array));
        builder.Allocate(no, factory.CreateOperation(), type, array, factory.Variable(copy));
        builder.Return(no, factory.CreateOperation(), factory.Variable(array));
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
    }

    [Test]
    public void HavocCannotReuseAnEarlierConstantGuard()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        builder.Assign(entry, factory.CreateOperation(), condition, factory.Boolean(true));
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, condition);
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(condition), yes, no);
        builder.Return(yes, factory.CreateOperation());
        builder.Return(no, factory.CreateOperation());
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.Null);
    }

    [Test]
    public void InputDependentGuardDoesNotSelectASampledPath()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(factory.CreateVariable("input", factory.BooleanType)), yes, no);
        builder.Return(yes, factory.CreateOperation());
        builder.Return(no, factory.CreateOperation());
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("MemoryControlFlowUnsupported"));
    }

    [Test]
    public void IncompatibleLayoutCannotSupplyNumbers()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        builder.Allocate(entry, factory.CreateOperation(), type, factory.CreateVariable("array", type), factory.Integer(1));
        builder.Return(entry, factory.CreateOperation());
        var result = ResourceMemoryProgram.Evaluate(builder.Build(), new("CoreCLR", "8.0.32", "arm64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.Null);
    }
}
