using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceMemoryProgramTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

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
