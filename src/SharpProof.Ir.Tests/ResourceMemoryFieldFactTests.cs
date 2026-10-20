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
}
