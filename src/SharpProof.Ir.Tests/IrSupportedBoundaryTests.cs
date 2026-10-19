using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrSupportedBoundaryTests
{
    [Test]
    public void ClosureFramesKeepImmutableScopedStorageCertificates()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Frame");
        Assert.That(factory.TryGetClosureFrameType(type, out _), Is.False);
        factory.RegisterClosureFrameType(type, [ResourceStorageKind.Word32, ResourceStorageKind.Reference]);
        factory.RegisterClosureFrameType(type, [ResourceStorageKind.Word32, ResourceStorageKind.Reference]);
        Assert.That(factory.TryGetClosureFrameType(type, out var fields), Is.True);
        Assert.That(fields, Is.EqualTo(new[] { ResourceStorageKind.Word32, ResourceStorageKind.Reference }));
        Assert.That(factory.CreateReferenceValue(type, new object()).Type, Is.EqualTo(type));
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterClosureFrameType(type, [ResourceStorageKind.Word64]); }));
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterDelegateReferenceType(type); }));
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterInterfaceReferenceType(type); }));
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterClosedClassReferenceType(type, null); }));
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterClosedSealedReferenceType(type); }));
        var other = new IrFactory(IrExecutionSemantics.Total);
        Assert.Throws<ArgumentException>((Action)(() => { other.RegisterClosureFrameType(type, [ResourceStorageKind.Word32]); }));
        Assert.Throws<ArgumentException>((Action)(() => { other.TryGetClosureFrameType(type, out _); }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void ClosureFramesRejectUnsupportedOrContradictoryStorage(int kind)
    {
        var factory = new IrFactory(kind == 0 ? IrExecutionSemantics.Legacy : IrExecutionSemantics.Total);
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Frame");
        if (kind == 4)
        { factory.RegisterDelegateReferenceType(type); }
        if (kind == 5)
        { factory.RegisterClosedSealedReferenceType(type); }
        if (kind == 6)
        { factory.RegisterClosedClassReferenceType(type, null); }
        var fields = kind == 1 ? ImmutableArray<ResourceStorageKind>.Empty :
            kind == 2 ? ImmutableArray.Create(ResourceStorageKind.Unspecified) : ImmutableArray.Create(ResourceStorageKind.Word32);
        Assert.Throws(kind == 2 ? Is.TypeOf<ArgumentOutOfRangeException>() : Is.TypeOf<ArgumentException>(),
            (Action)(() => { factory.RegisterClosureFrameType(kind == 3 ? factory.ObjectType : type, fields); }));
    }
    [Test]
    public void DelegateCertificatesAreScopedAndExcludeClassAndInterfaceCertificates()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Delegate");
        Assert.That(factory.IsDelegateReferenceType(type), Is.False);
        factory.RegisterDelegateReferenceType(type);
        factory.RegisterDelegateReferenceType(type);
        Assert.That(factory.IsDelegateReferenceType(type), Is.True);
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterInterfaceReferenceType(type); }));
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterClosedClassReferenceType(type, null); }));
        var other = new IrFactory(IrExecutionSemantics.Total);
        Assert.Throws<ArgumentException>((Action)(() => { other.RegisterDelegateReferenceType(type); }));
        Assert.Throws<ArgumentException>((Action)(() => { other.IsDelegateReferenceType(type); }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void InvalidDelegateCertificatesAreRejected(int kind)
    {
        var factory = new IrFactory(kind == 0 ? IrExecutionSemantics.Legacy : IrExecutionSemantics.Total);
        var nominal = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Reference");
        if (kind == 5)
        { factory.RegisterInterfaceReferenceType(nominal); }
        if (kind == 6)
        { factory.RegisterClosedClassReferenceType(nominal, null); }
        var type = kind switch
        {
            1 => factory.ObjectType,
            2 => factory.StringType,
            3 => factory.IntegerType,
            4 => factory.GetOrCreateSequenceType(factory.IntegerType),
            _ => nominal
        };
        Assert.Throws<ArgumentException>((Action)(() => { factory.RegisterDelegateReferenceType(type); }));
    }
    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    public void SourcePositionsRequireBothCoordinates(int line, int column)
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => { _ = new IrSourceSpan("source.cs", 0, 1, line, column); }));
        var valid = new IrSourceSpan("source.cs", 0, 1, 1, 1);
        Assert.That(valid.Column, Is.EqualTo(1));
    }

    [Test]
    public void PreconditionNamesRejectOversizedAndMalformedUtf16Evidence()
    {
        var span = new IrSourceSpan("source.cs", 4, 2);
        var hash = new string('a', 64);
        foreach (var identity in new[] { new string('x', 4097), new string('\u0800', 1366), "\ud800" })
        {
            Assert.That(IrCallPreconditionMarker.TryCreateName(identity, 0, span, span, out var sourceName), Is.False);
            Assert.That(sourceName, Is.Empty);
            Assert.That(IrCallPreconditionMarker.TryCreateMetadataName(identity, 0, span, hash, out var metadataName), Is.False);
            Assert.That(metadataName, Is.Empty);
        }
        Assert.That(IrCallPreconditionMarker.TryCreateName("callee", 0, span, span, out var valid), Is.True);
        Assert.That(IrCallPreconditionMarker.IsReservedName(valid), Is.True);
        Assert.That(IrCallPreconditionMarker.TryCreateMetadataName("callee", 0, span, hash, out valid), Is.True);
        Assert.That(valid, Does.EndWith(hash));
        Assert.That(IrCallPreconditionMarker.TryCreateMetadataName("callee", 0, span, new string('A', 64), out _), Is.False);
    }

    [Test]
    public void EmptyDisjunctionAndGuardRetainBooleanSemantics()
    {
        var factory = new IrFactory();
        Assert.That(IrSemanticTerms.Disjoin(factory, []), Is.SameAs(factory.Boolean(false)));
        Assert.That(IrSemanticTerms.Guard(factory, factory.Boolean(false), factory.Boolean(false)), Is.SameAs(factory.Boolean(true)));
        Assert.Throws<ArgumentNullException>((Action)(() => IrSemanticTerms.Guard(null!, factory.Boolean(true), factory.Boolean(true))));
    }

    [Test]
    public void SubstitutionReportsResidualFreeVariables()
    {
        var factory = new IrFactory();
        var first = factory.CreateVariable("first", factory.IntegerType);
        var second = factory.CreateVariable("second", factory.IntegerType);
        var sum = factory.Binary(IrBinaryOperator.Add, factory.Variable(first), factory.Variable(second));
        Assert.That(IrSubstitution.TrySubstitute(factory, sum,
            new Dictionary<IrVarId, IrTerm> { [first] = factory.Integer(2) }, new HashSet<IrVarId> { second }, out var valid), Is.True);
        Assert.That(IrSubstitution.TrySubstitute(factory, sum,
            new Dictionary<IrVarId, IrTerm> { [first] = factory.Integer(2) }, new HashSet<IrVarId>(), out _), Is.False);
        var result = new IrInterpreter(factory).Evaluate(valid, new Dictionary<IrVarId, IrValue> { [second] = factory.CreateIntegerValue(3) });
        Assert.That(result.Value!.Integer, Is.EqualTo(5));
    }

    [Test]
    public void BuilderRejectsForeignBlocksAndWrongCallTargets()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var foreign = new IrProgramBuilder(factory).CreateBlock();
        Assert.Throws<ArgumentException>((Action)(() => builder.SetEntry(foreign)));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Value", factory.IntegerType, true);
        var target = factory.CreateVariable("boolean", factory.BooleanType);
        Assert.Throws<ArgumentException>((Action)(() => builder.Call(entry, factory.CreateOperation(), target, member, null)));
        builder.Return(entry, factory.CreateOperation(), factory.Integer(5));
        var program = builder.Build();
        Assert.Throws<ArgumentException>((Action)(() => new IrProgramInterpreter(new IrFactory()).Execute(program)));
        var valid = new IrProgramInterpreter(factory).Execute(program);
        Assert.That(valid.ReturnValue!.Integer, Is.EqualTo(5));
        Assert.Throws<ArgumentException>((Action)(() => new IrProgramInterpreter(factory).Execute(program,
            new Dictionary<IrVarId, IrValue> { [target] = factory.CreateIntegerValue(1) })));
    }

    [Test]
    public void InstanceAndStaticOpaqueCallsPrintReceiverAndEscapedText()
    {
        var factory = new IrFactory();
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Receiver");
        var receiver = factory.Variable(factory.CreateVariable("receiver", type));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), type, "Read", factory.StringType, false, factory.StringType);
        var text = factory.String("\"\\\n\r\t\u0001\u0800");
        var printed = new IrPrinter(factory).Print(factory.PureOpaque(member, receiver, text));
        Assert.That(printed, Does.Contain("; "));
        Assert.That(printed, Does.Contain("\\\"\\\\\\n\\r\\t\\u0001\\u0800"));
        var staticMember = factory.GetOrCreateMember(factory.CreateIdentity(), type, "Static", factory.StringType, true);
        Assert.Throws<ArgumentException>((Action)(() => factory.PureOpaque(staticMember, receiver)));
        Assert.That(new IrPrinter(factory).Print(factory.PureOpaque(staticMember, null)), Does.StartWith("pure:"));
    }

    [Test]
    public void ReferenceCertificatesRequireNominalTotalTypes()
    {
        var legacy = new IrFactory();
        Assert.Throws<ArgumentException>((Action)(() => legacy.RegisterClosedSealedReferenceType(legacy.ObjectType)));
        var total = new IrFactory(IrExecutionSemantics.Total);
        Assert.Throws<ArgumentException>((Action)(() => total.RegisterClosedSealedReferenceType(total.IntegerType)));
        var type = total.GetOrCreateReferenceType(total.CreateIdentity(), "Closed");
        total.RegisterClosedSealedReferenceType(type);
        Assert.That(total.IsClosedSealedReferenceType(type), Is.True);
    }

    [Test]
    public void ClassAndInterfaceCertificatesRequireConsistentNominalMetadata()
    {
        var legacy = new IrFactory();
        Assert.Throws<ArgumentException>((Action)(() => legacy.RegisterClosedClassReferenceType(legacy.ObjectType, null)));
        Assert.Throws<ArgumentException>((Action)(() => legacy.RegisterInterfaceReferenceType(legacy.ObjectType)));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parent = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Parent");
        var child = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Child");
        var other = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Other");
        var face = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Interface");
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterClosedClassReferenceType(factory.IntegerType, null)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterClosedClassReferenceType(child, child)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterClosedClassReferenceType(child, factory.ObjectType)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterClosedClassReferenceType(child, parent)));
        factory.RegisterClosedClassReferenceType(parent, null);
        factory.RegisterClosedClassReferenceType(child, parent);
        factory.RegisterClosedClassReferenceType(child, parent);
        factory.RegisterClosedClassReferenceType(other, null);
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterClosedClassReferenceType(child, other)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterInterfaceReferenceType(child)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterInterfaceReferenceType(factory.IntegerType)));
        factory.RegisterInterfaceReferenceType(face);
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterClosedClassReferenceType(face, null)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterImplementedReferenceInterface(child, other)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterImplementedReferenceInterface(face, face)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterImplementedReferenceInterface(factory.ObjectType, face)));
        Assert.Throws<ArgumentException>((Action)(() => factory.RegisterImplementedReferenceInterface(factory.IntegerType, face)));
        factory.RegisterImplementedReferenceInterface(child, face);
        factory.RegisterImplementedReferenceInterface(child, face);
        Assert.That(factory.GetImplementedReferenceInterfaces(child), Is.EqualTo(new[] { face }));
        Assert.That(factory.GetImplementedReferenceInterfaces(parent), Is.Empty);
        Assert.That(factory.IsInterfaceReferenceType(face), Is.True);
        Assert.That(factory.IsInterfaceReferenceType(parent), Is.False);
        Assert.That(factory.TryGetClosedClassReferenceBase(child, out var baseType), Is.True);
        Assert.That(baseType, Is.EqualTo(parent));
        Assert.That(factory.TryGetClosedClassReferenceBase(face, out _), Is.False);
    }
}
