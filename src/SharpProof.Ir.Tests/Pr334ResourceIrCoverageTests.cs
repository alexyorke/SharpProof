using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr334ResourceIrCoverageTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

    [TestCase(-1)]
    [TestCase(ResourceAllocationShape.MaximumFields)]
    public void ClosureCaptureSlotsCannotEscapeTheirCertifiedFrame(int slot)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrClosureFrameSite.FieldName(slot)));
        Assert.That(error!.ParamName, Is.EqualTo("slot"));
        Assert.That(IrClosureFrameSite.FieldName(ResourceAllocationShape.MaximumFields - 1),
            Does.EndWith("$closure-capture:1023"));
    }

    [Test]
    public void LegacyIntegersCannotCertifyAClosureCaptureStorageWidth()
    {
        var factory = new IrFactory();
        var frameType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Frame");
        var frame = factory.CreateVariable("frame", frameType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var allocation = builder.Allocate(entry, factory.CreateOperation(IrClosureFrameSite.Description), frameType, frame);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            IrClosureFrameSite.FieldName(0), factory.IntegerType, false);
        var store = builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field,
            factory.Cast(factory.ObjectType, factory.Variable(frame)), field, factory.Integer(1));

        var admitted = IrClosureFrameSite.TryGetCapture(factory, allocation, store, 0,
            ResourceStorageKind.Word32, out var captured);

        Assert.That(factory.GetTypeInfo(factory.IntegerType).Width, Is.Zero);
        Assert.That(admitted, Is.False);
        Assert.That(captured.IsDefault, Is.True);
    }

    [Test]
    public void DetachedUnknownTypeCannotCertifyClosureStorage()
    {
        // Public factories reject undefined type kinds. Exercise the detached
        // record boundary without corrupting a shared built-in type singleton.
        var shapeConstructor = typeof(IrNullableTypeShape).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var shape = (IrTypeShape)shapeConstructor.Invoke([(IrTypeKind)999]);
        var factory = new IrFactory();
        var builtIn = factory.GetTypeInfo(factory.IntegerType);
        var type = new IrTypeInfo(builtIn.Id, builtIn.Name, shape, null);

        Assert.That(InvokePrivate<ResourceStorageKind>(typeof(IrClosureFrameSite), "Storage", type),
            Is.EqualTo(ResourceStorageKind.Unspecified));
    }

    [Test]
    public void DetachedAllocationShapeStillRequiresAnInitializedFieldSnapshot()
    {
        var constructor = typeof(ResourceAllocationShape).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        Action construct = () => constructor.Invoke([ResourceAllocationKind.Object, ResourceExpression.One,
            ResourceStorageKind.Unspecified, default(ImmutableArray<ResourceStorageKind>)]);

        var error = Assert.Throws<TargetInvocationException>(construct);
        Assert.That(error!.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(((ArgumentException)error.InnerException!).ParamName, Is.EqualTo("fields"));
        Assert.That(ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object([]), Layout,
            TestContext.CurrentContext.CancellationToken).Verified, Is.True);
    }

    [Test]
    public void MissingClosureCapturesAreDifferentFromAnInitializedEmptyFrame()
    {
        var error = Assert.Throws<ArgumentException>((Action)(() => ResourceAllocationShape.Closure(default)));
        Assert.That(error!.ParamName, Is.EqualTo("captures"));
        var empty = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Closure([]), Layout,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(empty.Verified, Is.True, empty.UnknownReason);
        Assert.That(empty.Bounds!.Objects, Is.EqualTo(2));
    }

    [Test]
    public void NegativeDistributionOrdinalCannotBindAnExpectedCostToAnotherInput()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new ResourceBooleanDistribution(-1, "choice", 1, 3)));
        Assert.That(error!.ParamName, Is.EqualTo("ordinal"));
        var valid = new ResourceBooleanDistribution(0, "choice", 1, 3);
        Assert.That(valid.ExpectedUpper(ResourceExpression.Literal(2), ResourceExpression.Literal(8)).Constant,
            Is.EqualTo(new BigInteger(4)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void NonCanonicalDistributionNamesCannotBindAnExpectedCost(int scenario)
    {
        var name = scenario switch
        {
            0 => string.Empty,
            1 => "   ",
            2 => "choice\nother",
            3 => new string('n', ResourceExpression.MaximumTextLength + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            _ = new ResourceBooleanDistribution(0, name, 1, 3)));
        Assert.That(error!.ParamName, Is.EqualTo("name"));

        var valid = new ResourceBooleanDistribution(0, "choice", 1, 3);
        Assert.That(valid.ExpectedUpper(ResourceExpression.Literal(2), ResourceExpression.Literal(6)).Constant,
            Is.EqualTo(new BigInteger(3)));
    }

    [Test]
    public void DetachedUnknownStorageCannotProduceAByteFootprint()
    {
        var constructor = typeof(ResourceAllocationShape).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var shape = (ResourceAllocationShape)constructor.Invoke([ResourceAllocationKind.Array, ResourceExpression.One,
            (ResourceStorageKind)999, ImmutableArray<ResourceStorageKind>.Empty]);
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            ResourceRuntimeLayout.Bound(shape, Layout, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.ParamName, Is.EqualTo("storage"));
    }

    [Test]
    public void ObservedArraysCannotUseACorruptedIntegerStorageWidth()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var arrayType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", arrayType);
        builder.Allocate(entry, factory.CreateOperation(), arrayType, array, factory.Integer(1));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(array));
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 100, options,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Verified, Is.True);

        // Corrupt this fixture's owned metadata after observing the valid run.
        // Existing concrete values must not supply a storage-width verdict.
        typeof(IrTypeInfo).GetField("<Width>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(factory.GetTypeInfo(factory.IntegerType), 0);
        var rejected = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(rejected.Bounds, Is.Null);
        Assert.That(rejected.UnknownReason, Is.EqualTo("MemoryReplayAllocationUnsupported"));
    }

    [Test]
    public void UnknownObservedTypeCannotBeReclassifiedFromItsRuntimeValue()
    {
        Assert.That(InvokePrivate<bool>(typeof(ResourceMemoryReplay), "ObservedKindMatches",
            (IrTypeKind)999, IrValueKind.Integer), Is.False);
        Assert.That(InvokePrivate<bool>(typeof(ResourceMemoryReplay), "ObservedKindMatches",
            IrTypeKind.Integer, IrValueKind.Integer), Is.True);
    }

    [TestCase(32, true)]
    [TestCase(33, false)]
    public void ReferenceResolutionHonorsItsDepthBoundary(int depth, bool accepted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("owner", factory.ObjectType);
        var value = factory.CreateReferenceValue(factory.ObjectType, new object());
        var values = new Dictionary<IrVarId, IrValue> { [variable] = value };
        var resolved = InvokePrivate<IrValue?>(typeof(ResourceMemoryReplay), "ActualReference",
            factory.Variable(variable), factory, values, depth);

        Assert.That(resolved, accepted ? Is.SameAs(value) : Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PrimitiveAndNullObservationsCannotInventAnOwnedObjectIdentity(bool nullValue)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var owned = factory.CreateReferenceValue(factory.ObjectType, new object());
        var identities = new List<IrValue> { owned };
        var value = nullValue ? factory.CreateNullValue(factory.ObjectType) : factory.CreateIntegerValue(0);

        Assert.That(InvokePrivate<int>(typeof(ResourceMemoryReplay), "Find", value, identities), Is.Zero);
        Assert.That(InvokePrivate<int>(typeof(ResourceMemoryReplay), "Find", owned, identities), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MaximalObservedTraceKeepsTheLifetimeEnvelopeBounded(bool oneTooMany)
    {
        var instructionBudget = ResourceMemoryTrace.MaximumEvents / 4;
        var allocations = instructionBudget - (oneTooMany ? 0 : 1);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        for (var index = 0; index < allocations; index++)
        {
            builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType,
                factory.CreateVariable("temporary" + index, factory.ObjectType));
        }
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var observations = 0;
        var options = new IrProgramReplayOptions(static _ => null)
        {
            CompletedInstructionObserver = (instruction, value, approximation) =>
            {
                ArgumentNullException.ThrowIfNull(instruction);
                observations++;
                replay.Observe(instruction, value, approximation);
            }
        };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options,
            TestContext.CurrentContext.CancellationToken);
        var check = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);

        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(observations, Is.EqualTo(instructionBudget + (oneTooMany ? 1 : 0)));
        if (oneTooMany)
        {
            Assert.That(check.Bounds, Is.Null);
            Assert.That(check.UnknownReason, Is.EqualTo("MemoryReplayBudgetExceeded"));
        }
        else
        {
            // 255 dead allocations plus the final return: one anchor and
            // two events per allocation, or 511 events within the 1024 limit.
            Assert.That(check.UnknownReason, Is.Null);
            Assert.That(check.Bounds, Is.EqualTo(new ResourceMemoryBounds(
                allocations * 24, allocations * 24, 24, 24, 0, 0)));
        }
    }

    [Test]
    public void GlobalTraceAdmissionStillRejectsAnOversizedOtherwiseValidTrace()
    {
        var events = Enumerable.Range(1, ResourceMemoryTrace.MaximumEvents + 1)
            .Select(static identity => new ResourceMemoryEvent(ResourceMemoryEventKind.Allocate,
                identity, identity, 0, 24, 24)).ToImmutableArray();
        var check = ResourceMemoryTrace.Evaluate(events, 0, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("MemoryTraceBudgetExceeded"));
    }

    [Test]
    public void DetachedEarlierReturnCannotPublishAPartialAllocationEnvelope()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var target = factory.CreateVariable("allocated", factory.ObjectType);
        var allocation = builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType, target);
        var earlier = builder.Return(entry, factory.CreateOperation());
        var valid = builder.Build();
        var final = new IrReturnInstruction(new IrInstructionId(valid.Scope, 2), factory.CreateOperation(), null);
        var detachedBlock = new IrBasicBlock(entry, null, [allocation, earlier, final]);
        var detached = new IrProgram(factory, valid.Scope, entry, [detachedBlock]);
        var replay = new ResourceMemoryReplay(detached);
        replay.Observe(allocation, factory.CreateReferenceValue(factory.ObjectType, new object()), false);
        replay.Observe(earlier, null, false);
        replay.Observe(final, null, false);
        var alleged = new IrProgramExecutionResult(IrProgramExecutionStatus.Returned, null, final, null, null,
            ImmutableDictionary<IrVarId, IrValue>.Empty, 3);

        var check = replay.Complete(alleged, Layout, TestContext.CurrentContext.CancellationToken);

        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("MemoryReplayInstructionUnsupported"));
    }

    [Test]
    public void DetachedNullOperandCannotPublishAPartialAsymptoticOrder()
    {
        var expression = DetachedBinary(ResourceExpressionKind.Add, ResourceExpression.One, ResourceExpression.One);
        typeof(ResourceExpression).GetField("<Operands>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(expression, ImmutableArray.Create<ResourceExpression>(ResourceExpression.One, null!));
        var growth = ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken);
        Assert.That(growth.Order, Is.Null);
        Assert.That(growth.Input, Is.Null);
        Assert.That(growth.UnknownReason, Is.EqualTo("MalformedAsymptoticExpression"));
    }

    [TestCase((int)ResourceExpressionKind.Multiply, "0", true)]
    [TestCase((int)ResourceExpressionKind.Add, "n", false)]
    [TestCase((int)ResourceExpressionKind.Maximum, "n", false)]
    public void DetachedZeroArithmeticRetainsItsMathematicalGrowth(int kind, string expected, bool zero)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "length", 100));
        var expression = DetachedBinary((ResourceExpressionKind)kind, input, ResourceExpression.Zero);
        var growth = ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken);

        Assert.That(growth.UnknownReason, Is.Null);
        Assert.That(growth.Order, Is.EqualTo(expected));
        Assert.That(growth.IsZero, Is.EqualTo(zero));
        foreach (var size in new[] { 0, 1, 16, 100 })
        {
            Assert.That(expression.Evaluate(_ => size, TestContext.CurrentContext.CancellationToken),
                Is.EqualTo(new BigInteger(zero ? 0 : size)));
        }
    }

    private static ResourceExpression DetachedBinary(ResourceExpressionKind kind,
        ResourceExpression left, ResourceExpression right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var constructor = typeof(ResourceExpression).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (ResourceExpression)constructor.Invoke([kind, BigInteger.Zero, null,
            ImmutableArray.Create(left, right), null]);
    }

    private static T InvokePrivate<T>(Type type, string method, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);
        var member = type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!;
        return (T)member.Invoke(null, arguments)!;
    }
}
