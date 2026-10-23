using System.Collections.Immutable;
using System.Numerics;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceRepresentationBoundaryGapTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

    [Test]
    public void ProbabilityInputOrdinalsStartAtZeroAndPreserveSymbolicWeightedCosts()
    {
        Action negative = () => _ = new ResourceBooleanDistribution(-1, "selected", 1, 3);
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(negative)!.ParamName, Is.EqualTo("ordinal"));
        var distribution = new ResourceBooleanDistribution(0, "selected", 1, 3);
        Assert.That(distribution.Ordinal, Is.Zero);
        Assert.That(distribution.Name, Is.EqualTo("selected"));
        var size = ResourceExpression.InputSize(new ResourceInput(1, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var selected = ResourceExpression.Add(ResourceExpression.Multiply(ResourceExpression.Literal(2), size), ResourceExpression.One);
        var expected = distribution.ExpectedUpper(size, selected);
        Assert.That(expected.Constant, Is.Null);
        foreach (var length in new[] { 0, 1, 7, 17 })
        {
            var independentlyRounded = (5L * length + 1 + 3) / 4;
            Assert.That(expected.Evaluate(_ => length, TestContext.CurrentContext.CancellationToken),
                Is.EqualTo(new BigInteger(independentlyRounded)));
        }
    }

    [Test]
    public void MissingCaptureStorageDiffersFromAnEmptyOrMaximumSizedClosure()
    {
        Action missing = () => ResourceAllocationShape.Closure(default);
        Assert.That(Assert.Throws<ArgumentException>(missing)!.ParamName, Is.EqualTo("captures"));
        var empty = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Closure([]), Layout,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(empty.Verified, Is.True, empty.UnknownReason);
        Assert.That(empty.Bounds!.Objects, Is.EqualTo(2));
        Assert.That(empty.Bounds.Lower.Constant, Is.EqualTo(new BigInteger(88)));
        Assert.That(empty.Bounds.Upper.Constant, Is.EqualTo(new BigInteger(88)));

        var captures = Enumerable.Repeat(ResourceStorageKind.Byte, ResourceAllocationShape.MaximumFields).ToImmutableArray();
        var full = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Closure(captures), Layout,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(full.Verified, Is.True, full.UnknownReason);
        Assert.That(full.Bounds!.Objects, Is.EqualTo(2));
        Assert.That(full.Bounds.Lower.Constant, Is.EqualTo(new BigInteger(1104)));
        Assert.That(full.Bounds.Upper.Constant, Is.EqualTo(new BigInteger(8272)));
        Action beyondLimit = () => ResourceAllocationShape.Closure(captures.Add(ResourceStorageKind.Byte));
        Assert.Throws<ResourceExpressionLimitException>(beyondLimit);
    }

    [Test]
    public void CaptureSlotNamesAreDistinctOnlyWithinTheCertificateExtent()
    {
        var first = IrClosureFrameSite.FieldName(0);
        var last = IrClosureFrameSite.FieldName(ResourceAllocationShape.MaximumFields - 1);
        Assert.That(first, Is.EqualTo(IrFieldSites.Prefix + "$closure-capture:0"));
        Assert.That(last, Is.EqualTo(IrFieldSites.Prefix + "$closure-capture:1023"));
        Assert.That(last, Is.Not.EqualTo(first));
        Action negative = () => IrClosureFrameSite.FieldName(-1);
        Action pastEnd = () => IrClosureFrameSite.FieldName(ResourceAllocationShape.MaximumFields);
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(negative)!.ParamName, Is.EqualTo("slot"));
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(pastEnd)!.ParamName, Is.EqualTo("slot"));
    }

    [Test]
    public void StandaloneCaptureHelperRejectsLegacyIntegersWithoutPrimitiveStorage()
    {
        // This is standalone helper input validation using builder-valid Legacy
        // IR, not a registered Total frame certificate or a lifetime proof.
        var factory = new IrFactory(IrExecutionSemantics.Legacy);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var frameType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Frame");
        var frame = factory.CreateVariable("frame", frameType);
        var allocation = builder.Allocate(entry, factory.CreateOperation(IrClosureFrameSite.Description), frameType, frame);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            IrClosureFrameSite.FieldName(0), factory.IntegerType, false);
        var receiver = factory.Cast(factory.ObjectType, factory.Variable(frame));
        var store = builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field,
            receiver, field, factory.Integer(1));
        builder.Return(entry, factory.CreateOperation());
        _ = builder.Build();
        Assert.That(factory.GetTypeInfo(factory.GetMemberInfo(field).ReturnType).Width, Is.Zero);
        Assert.That(IrClosureFrameSite.Matches(factory, allocation), Is.True);
        Assert.That(IrClosureFrameSite.TryGetCapture(factory, allocation, store, 0,
            ResourceStorageKind.Word32, out var captured), Is.False);
        Assert.That(captured.IsDefault, Is.True);
    }

    [TestCase(false, 0, 24)]
    [TestCase(false, 1, 32)]
    [TestCase(false, 17, 96)]
    [TestCase(true, 0, 0)]
    [TestCase(true, 1, 24)]
    [TestCase(true, 17, 56)]
    public void InputDependentAllocationsRetainTheirReturnedAliasAfterRootOverwrite(bool text, int length, int retained)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var type = text ? factory.StringType : array;
        var input = factory.CreateVariable("length", factory.IntegerType);
        var root = factory.CreateVariable("root", type);
        var alias = factory.CreateVariable("alias", factory.ObjectType);
        var temporary = factory.CreateVariable("temporary", array);
        var character = factory.Integer(factory.GetOrCreateIntegerType(16, signed: false), 'x');
        var allocation = builder.Allocate(entry, factory.CreateOperation(), type, root, factory.Variable(input), text ? [character] : []);
        builder.Assign(entry, factory.CreateOperation(), alias, factory.Cast(factory.ObjectType, factory.Variable(root)));
        builder.Assign(entry, factory.CreateOperation(), root, factory.Null(type));
        builder.Allocate(entry, factory.CreateOperation(), array, temporary, factory.Integer(3));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(alias));
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        IrValue? allocated = null;
        var options = new IrProgramReplayOptions(static _ => null)
        {
            CompletedInstructionObserver = (instruction, value, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                if (ReferenceEquals(instruction, allocation))
                { allocated = value; }
                replay.Observe(instruction, value, approximation);
            }
        };
        var inputs = ImmutableDictionary<IrVarId, IrValue>.Empty.Add(input, factory.CreateIntegerValue(length));
        var execution = new IrProgramInterpreter(factory).Execute(program, inputs, 1000, options,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(allocated, Is.Not.Null);
        var allocatedValue = allocated!;
        Assert.That(allocatedValue.Kind, Is.EqualTo(text ? IrValueKind.String : IrValueKind.Sequence));
        Assert.That(execution.GetCurrentValue(root)!.Kind, Is.EqualTo(IrValueKind.Null));
        var returned = execution.ReturnValue!;
        Assert.That(returned.Kind, Is.EqualTo(IrValueKind.Reference));
        var identity = returned.Reference;
        Assert.That(identity, Is.SameAs(execution.GetCurrentValue(alias)!.Reference));
        Assert.That(identity, Is.SameAs(text ? (object)allocatedValue.String : allocatedValue.SequenceIdentity));
        if (text)
        {
            Assert.That(identity, Is.TypeOf<string>());
            var content = (string)identity;
            Assert.That(content.Length, Is.EqualTo(length));
            Assert.That(content, Is.EqualTo(new string('x', length)));
            Assert.That(ReferenceEquals(content, string.Empty), Is.EqualTo(length == 0));
        }
        else
        {
            Assert.That(identity, Is.TypeOf<IrValue>());
            var sequence = (IrValue)identity;
            Assert.That(sequence.Kind, Is.EqualTo(IrValueKind.Sequence));
            Assert.That(sequence.Elements.Length, Is.EqualTo(length));
            Assert.That(execution.Heap, Is.Not.Null);
            var heap = execution.Heap!;
            Assert.That(heap.IsFreshReceiver(sequence), Is.True);
            for (var index = 0; index < length; index++)
            {
                Assert.That(heap.TryReadElement(sequence, index, out var element), Is.True);
                Assert.That(element.Kind, Is.EqualTo(IrValueKind.Integer));
                Assert.That(element.IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
            }
            Assert.That(heap.ConsumedApproximation, Is.False);
        }
        var observed = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(observed.Verified, Is.True, observed.UnknownReason);
        Assert.That(observed.Bounds, Is.EqualTo(new ResourceMemoryBounds(retained + 40, retained + 40, 40, 40, retained, retained)));
        Assert.That(ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }
}
