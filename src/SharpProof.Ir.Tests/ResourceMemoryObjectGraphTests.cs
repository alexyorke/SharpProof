using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceMemoryObjectGraphTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

    [TestCase(false, 56)]
    [TestCase(true, 64)]
    public void OwnedFieldGraphsCountSharedArraysOnce(bool shared, int bytes)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var node = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Node");
        factory.RegisterClosedClassReferenceType(node, null);
        factory.RegisterObjectStorageType(node, shared ? [ResourceStorageKind.Reference, ResourceStorageKind.Reference] : [ResourceStorageKind.Reference]);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var first = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, IrFieldSites.Prefix + "first", array, false);
        var second = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, IrFieldSites.Prefix + "second", array, false);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var root = factory.CreateVariable("root", node);
        var child = factory.CreateVariable("child", array);
        builder.Allocate(entry, factory.CreateOperation(), node, root);
        builder.Allocate(entry, factory.CreateOperation(), array, child, factory.Integer(1));
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Cast(factory.ObjectType, factory.Variable(root)), first, factory.Variable(child));
        if (shared)
        { builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Cast(factory.ObjectType, factory.Variable(root)), second, factory.Variable(child)); }
        builder.Return(entry, factory.CreateOperation(), factory.Variable(root));
        var program = builder.Build();
        var result = ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(Observed(program).Bounds, Is.EqualTo(result.Bounds));
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(bytes, bytes, 0, 0, bytes, bytes)));
    }

    [Test]
    public void ObjectCyclesRemainFiniteAndFieldOverwriteReleasesOldChildren()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var node = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Node");
        factory.RegisterClosedClassReferenceType(node, null);
        factory.RegisterObjectStorageType(node, [ResourceStorageKind.Reference, ResourceStorageKind.Reference]);
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var next = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, IrFieldSites.Prefix + "next", node, false);
        var data = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, IrFieldSites.Prefix + "data", array, false);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var root = factory.CreateVariable("root", node);
        var child = factory.CreateVariable("child", array);
        builder.Allocate(entry, factory.CreateOperation(), node, root);
        var receiver = factory.Cast(factory.ObjectType, factory.Variable(root));
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, receiver, next, factory.Variable(root));
        builder.Allocate(entry, factory.CreateOperation(), array, child, factory.Integer(1));
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, receiver, data, factory.Variable(child));
        builder.Allocate(entry, factory.CreateOperation(), array, child, factory.Integer(3));
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, receiver, data, factory.Variable(child));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(root));
        var program = builder.Build();
        var result = ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(Observed(program).Bounds, Is.EqualTo(result.Bounds));
        Assert.That(result.Bounds, Is.EqualTo(new ResourceMemoryBounds(104, 104, 32, 32, 72, 72)));
    }

    [Test]
    public void CompleteStorageIsImmutableAndRequiresADirectClass()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var node = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Node");
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterObjectStorageType(node, [])));
        factory.RegisterClosedClassReferenceType(node, null);
        factory.RegisterObjectStorageType(node, [ResourceStorageKind.Word32, ResourceStorageKind.Reference]);
        Assert.That(factory.TryGetObjectStorageType(node, out var fields), Is.True);
        Assert.That(fields, Is.EqualTo(ImmutableArray.Create(ResourceStorageKind.Word32, ResourceStorageKind.Reference)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterObjectStorageType(node, [])));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterObjectStorageType(factory.ObjectType, [])));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterObjectStorageType(node, default)));
    }
    private static ResourceMemoryCheck Observed(IrProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(program.Factory).Execute(program, null, 1000, options, TestContext.CurrentContext.CancellationToken);
        return replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
    }
}
