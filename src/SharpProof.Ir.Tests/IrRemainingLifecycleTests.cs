using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrRemainingLifecycleTests
{
    [Test]
    public void ScalarBoundaryHelpersRejectOverflowAndUnsupportedOperatorFamilies()
    {
        Assert.That(IrScalarOperations.TryNegate(long.MinValue, out var overflow), Is.False);
        Assert.That(overflow, Is.Zero);
        Assert.That(IrScalarOperations.TryNegate(-3, out var result), Is.True);
        Assert.That(result, Is.EqualTo(3));
        Assert.That(IrScalarOperations.Evaluate(IrBinaryOperator.AndAlso, 1, 1).Kind, Is.EqualTo(IrScalarResultKind.Unsupported));
        var factory = new IrFactory();
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOperatorCatalog.GetBuiltInType(factory, IrTypeKind.Reference)));
        Assert.Throws<ArgumentException>((Action)(() => factory.Binary(IrBinaryOperator.AndAlso, factory.Integer(1), factory.Boolean(true))));
    }

    [Test]
    public void PublicStoreMetadataRetainsElementKindAndIntegerSignedness()
    {
        var factory = new IrFactory();
        var integer = (IrIntegerTerm)factory.Integer(-2);
        Assert.That(integer.Signed, Is.True);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var array = factory.Variable(factory.CreateVariable("array", factory.GetOrCreateSequenceType(factory.IntegerType)));
        var store = builder.ElementStore(block, factory.CreateOperation(), array, factory.Integer(0), integer);
        Assert.That(store.IsElementStore, Is.True);
        Assert.That(store.IsFieldStore, Is.False);
        Assert.That(store.IsStore, Is.True);
        builder.Return(block, factory.CreateOperation());
        Assert.That(builder.Build().Blocks.Length, Is.EqualTo(1));
    }

    [Test]
    public void MemberSignaturesAndFieldStoresRejectIncompatiblePublicInputs()
    {
        var factory = new IrFactory();
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Owner");
        var receiver = factory.Variable(factory.CreateVariable("owner", type));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), type, "Read", factory.IntegerType, false, factory.IntegerType);
        Assert.Throws<ArgumentException>((Action)(() => factory.PureOpaque(member, receiver)));
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), type, "field:value", factory.IntegerType, true);
        Assert.Throws<ArgumentException>((Action)(() => builder.FieldStore(block, factory.CreateOperation(), IrWriteRegion.Field, receiver, field, factory.Integer(1))));
        builder.Return(block, factory.CreateOperation());
        Assert.That(builder.Build().Blocks.Length, Is.EqualTo(1));
    }

    [Test]
    public void PreconditionMarkersRequirePositiveSpanAndBoundedClauseOrdinal()
    {
        var span = new IrSourceSpan("source.cs", 1, 2);
        Assert.That(IrCallPreconditionMarker.TryCreateName("callee", -1, span, span, out var rejected), Is.False);
        Assert.That(rejected, Is.Empty);
        Assert.That(IrCallPreconditionMarker.TryCreateName("callee", 4096, span, span, out _), Is.False);
        Assert.That(IrCallPreconditionMarker.TryCreateName("callee", 0, new IrSourceSpan("source.cs", 1, 0), span, out _), Is.False);
        Assert.That(IrCallPreconditionMarker.TryCreateName("callee", 4095, span, span, out var accepted), Is.True);
        Assert.That(accepted, Does.Contain(":4095:"));
    }

    [Test]
    public void SequenceLoadPreservesMissingReceiverFailure()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var array = factory.CreateVariable("array", factory.GetOrCreateSequenceType(factory.IntegerType));
        var target = factory.CreateVariable("target", factory.IntegerType);
        builder.Load(block, factory.CreateOperation(), target, builder.SequenceLocation(factory.Variable(array), factory.Integer(0)));
        builder.Return(block, factory.CreateOperation(), factory.Variable(target));
        var program = builder.Build();
        var interpreter = new IrProgramInterpreter(factory);
        Assert.That(interpreter.Execute(program).Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        var valid = interpreter.Execute(program, global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(array, factory.CreateSequenceValue(factory.GetVariableInfo(array).Type, [factory.CreateIntegerValue(7)])));
        Assert.That(valid.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(valid.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.UnsupportedOperation));
    }

    [Test]
    public void MemberStoreAndConditionPreserveMissingOperandFailures()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var missing = factory.CreateVariable("stored", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Static", factory.IntegerType, true);
        builder.Store(block, factory.CreateOperation(), builder.MemberLocation(member, null), factory.Variable(missing));
        builder.Return(block, factory.CreateOperation());
        Assert.That(new IrProgramInterpreter(factory).Execute(builder.Build()).Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        var conditionalBuilder = new IrProgramBuilder(factory);
        var entry = conditionalBuilder.CreateBlock();
        var target = conditionalBuilder.CreateBlock();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        conditionalBuilder.Branch(entry, factory.CreateOperation(), factory.Variable(condition), target, target);
        conditionalBuilder.Return(target, factory.CreateOperation());
        var program = conditionalBuilder.Build();
        var interpreter = new IrProgramInterpreter(factory);
        Assert.That(interpreter.Execute(program).Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(interpreter.Execute(program, global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(condition, factory.CreateBooleanValue(true))).Status,
            Is.EqualTo(IrProgramExecutionStatus.Returned));
    }
    [Test]
    public void StringCastToUnknownNominalTypeDoesNotInventRuntimeRelations()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Unrelated");
        var source = factory.String("text");
        var interpreter = new IrInterpreter(factory);
        var unsupported = interpreter.Evaluate(factory.Cast(type, source));
        Assert.That(unsupported.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(unsupported.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.UnsupportedCast));
        var widened = interpreter.Evaluate(factory.Cast(factory.ObjectType, source));
        Assert.That(widened.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(widened.Value!.Reference, Is.EqualTo("text"));
    }

    [Test]
    public void FieldSnapshotSelectionStopsWhenItsExistingWorkBudgetIsExhausted()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var value = factory.CreateReferenceValue(factory.ObjectType, new IrObjectState().WithField(field, factory.CreateIntegerValue(3)));
        var heap = new IrHeap();
        heap.Fields[(value.Reference, field)] = factory.CreateIntegerValue(7);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var inputBuilder = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        inputBuilder[flag] = factory.CreateBooleanValue(true);
        var leafBuilder = ImmutableArray.CreateBuilder<IrTerm>();
        var first = factory.CreateVariable("first", factory.ObjectType);
        inputBuilder.Add(first, value);
        leafBuilder.Add(factory.Variable(first));
        for (var index = 1; index < 2048; index++)
        {
            var owner = factory.CreateVariable("owner" + index, factory.ObjectType);
            inputBuilder.Add(owner, value);
            leafBuilder.Add(factory.Variable(owner));
        }
        var leaves = leafBuilder.ToImmutable();
        while (leaves.Length > 1)
        {
            var next = ImmutableArray.CreateBuilder<IrTerm>();
            for (var index = 0; index < leaves.Length; index += 2)
            { next.Add(factory.Conditional(factory.Variable(flag), leaves[index], leaves[index + 1])); }
            leaves = next.ToImmutable();
        }
        var inputs = inputBuilder.ToImmutable();
        var interpreter = new IrInterpreter(factory);
        var valid = interpreter.Evaluate(factory.PureOpaque(field, factory.Variable(first)), inputs, null, heap, default, [first]);
        Assert.That(valid.Value!.Integer, Is.EqualTo(3));
        var exhausted = interpreter.Evaluate(factory.PureOpaque(field, leaves[0]), inputs, null, heap, default, [first]);
        Assert.That(exhausted.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(exhausted.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.UnsupportedOperation));
        Assert.That(heap.ConsumedApproximation, Is.False);
    }
    [Test]
    public void FactoryInterningDistinguishesDifferentArgumentSequences()
    {
        var factory = new IrFactory();
        var values = Enumerable.Range(0, 398).Select(value => factory.Integer(value)).ToImmutableArray();
        Assert.That(values[397].Id.Value - values[0].Id.Value, Is.EqualTo(397));
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Pair", factory.IntegerType, true,
            factory.IntegerType, factory.IntegerType);
        var first = factory.PureOpaque(member, null, values[0], values[397]);
        var second = factory.PureOpaque(member, null, values[1], values[0]);
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Arguments, Is.EqualTo(ImmutableArray.Create(values[1], values[0])));
        Assert.That(factory.PureOpaque(member, null, values[0], values[397]), Is.SameAs(first));
    }

    [Test]
    public void MemberSignaturesDistinguishCollidingEmptyAndBooleanParameterKeys()
    {
        var factory = new IrFactory();
        Assert.That(factory.BooleanType.Value, Is.Zero);
        var identity = factory.CreateIdentity();
        var empty = factory.GetOrCreateMember(identity, factory.ObjectType, "Member", factory.IntegerType, true);
        var parameterized = factory.GetOrCreateMember(identity, factory.ObjectType, "Member", factory.IntegerType, true, factory.BooleanType);
        Assert.That(parameterized, Is.Not.EqualTo(empty));
        Assert.That(factory.GetMemberInfo(empty).ParameterTypes, Is.Empty);
        Assert.That(factory.GetMemberInfo(parameterized).ParameterTypes, Is.EqualTo(ImmutableArray.Create(factory.BooleanType)));
        Assert.That(factory.GetOrCreateMember(identity, factory.ObjectType, "Member", factory.IntegerType, true), Is.EqualTo(empty));
    }
}
