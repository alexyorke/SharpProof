using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CoreIrAdvisoryInterpreterTests
{
    [Test]
    public void ReplacementAndJoinContainConcreteExecutions()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var c = f.CreateVariable("c", f.BooleanType);
        var x = f.CreateVariable("x", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Assign(e, f.CreateOperation(), x, f.Integer(0));
        b.Assign(e, f.CreateOperation(), x, f.Conditional(f.Variable(c), f.Integer(1), f.Integer(2)));
        b.Return(e, f.CreateOperation(), f.Variable(x));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [c, x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Is.Empty);
        var interval = result.Outputs[0].Values[x];
        Assert.That(interval.Contains(0), Is.False);
        foreach (var input in new[] { false, true })
        { var concrete = new IrProgramInterpreter(f).Execute(program, new Dictionary<IrVarId, IrValue> { { c, f.CreateBooleanValue(input) } }); Assert.That(interval.Contains(concrete.ReturnValue!.Integer), Is.True); }
    }
    [Test]
    public void BranchEdgesRefineAndJoin()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var x = f.CreateVariable("x", f.IntegerType);
        var y = f.CreateVariable("y", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        var t = b.CreateBlock();
        var n = b.CreateBlock();
        var exit = b.CreateBlock();
        b.Branch(e, f.CreateOperation(), f.Binary(IrBinaryOperator.LessThan, f.Variable(x), f.Integer(5)), t, n);
        b.Assign(t, f.CreateOperation(), y, f.Variable(x));
        b.Goto(t, f.CreateOperation(), exit);
        b.Assign(n, f.CreateOperation(), y, f.Integer(0));
        b.Goto(n, f.CreateOperation(), exit);
        b.Return(exit, f.CreateOperation(), f.Variable(y));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [x, y]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(x, IntervalValue.Range(0, 10)));
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Inputs[1].Values[x].UpperBound, Is.EqualTo(4));
        Assert.That(result.Inputs[2].Values[x].LowerBound, Is.EqualTo(5));
        for (var input = 0; input <= 10; input++)
        { var concrete = new IrProgramInterpreter(f).Execute(program, new Dictionary<IrVarId, IrValue> { { x, f.CreateIntegerValue(input) } }); Assert.That(result.Outputs[3].Values[y].Contains(concrete.ReturnValue!.Integer), Is.True); }
    }
    [Test]
    public void WrappingAssignmentContainsConcreteByteResults()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var type = f.GetOrCreateIntegerType(8, false);
        var x = f.CreateVariable("x", type);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Assign(e, f.CreateOperation(), x, f.Binary(IrBinaryOperator.Add, f.Variable(x), f.Integer(type, 2)));
        b.Return(e, f.CreateOperation(), f.Variable(x));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(x, IntervalValue.Range(254, 255)));
        Assert.That(result.Gaps, Is.Empty);
        foreach (var input in new[] { 254, 255 })
        { var concrete = new IrProgramInterpreter(f).Execute(program, new Dictionary<IrVarId, IrValue> { { x, f.CreateIntegerValue(type, input) } }); Assert.That(result.Outputs[0].Values[x].Contains(concrete.ReturnValue!.Integer), Is.True); }
    }
    private static (IrProgram Program, IrVarId X) Loop()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var x = f.CreateVariable("x", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        var head = b.CreateBlock();
        var body = b.CreateBlock();
        var exit = b.CreateBlock();
        b.Goto(e, f.CreateOperation(), head);
        b.Branch(head, f.CreateOperation(), f.Binary(IrBinaryOperator.LessThan, f.Variable(x), f.Integer(5)), body, exit);
        b.Assign(body, f.CreateOperation(), x, f.Binary(IrBinaryOperator.Add, f.Variable(x), f.Integer(1)));
        b.Goto(body, f.CreateOperation(), head);
        b.Return(exit, f.CreateOperation(), f.Variable(x));
        return (b.Build(), x);
    }
    [Test]
    public void LoopWideningContainsConcreteReturns()
    {
        var (program, x) = Loop();
        var result = new CoreIrAdvisoryInterpreter(program, [x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(x, IntervalValue.Range(0, 5)));
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Iterations, Is.LessThan(1000));
        for (var input = 0; input <= 5; input++)
        { var concrete = new IrProgramInterpreter(program.Factory).Execute(program, new Dictionary<IrVarId, IrValue> { { x, program.Factory.CreateIntegerValue(input) } }); Assert.That(result.Outputs[3].Values[x].Contains(concrete.ReturnValue!.Integer), Is.True); }
    }
    [Test]
    public void IterationLimitIsExplicitlyIncomplete()
    { var (program, x) = Loop(); var result = new CoreIrAdvisoryInterpreter(program, [x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(x, IntervalValue.Range(0, 5)), 1); Assert.That(result.Gaps, Does.Contain("iteration limit")); Assert.That(result.Accepted, Is.False); Assert.That(result.Outputs, Is.Empty); Assert.That(result.TryGetOutput(0, out _), Is.False); Assert.Throws<InvalidOperationException>((Action)(() => result.RequireOutput(0))); }
    [Test]
    public void PreCancellationPropagates()
    {
        var (program, x) = Loop();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => new CoreIrAdvisoryInterpreter(program, [x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(x, IntervalValue.Range(0, 5)), token: cancellation.Token)));
    }
    [Test]
    public void UnsupportedOpaqueResultOverwritesOldFactAndReportsGap()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var x = f.CreateVariable("x", f.IntegerType);
        var member = f.GetOrCreateMember(f.CreateIdentity(), f.ObjectType, "Unknown", f.IntegerType, true, []);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Assign(e, f.CreateOperation(), x, f.Integer(1));
        b.Assign(e, f.CreateOperation(), x, f.PureOpaque(member, null));
        b.Return(e, f.CreateOperation(), f.Variable(x));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Does.Contain("unsupported term"));
        Assert.That(result.Outputs[0].Values[x], Is.EqualTo(IntervalValue.Range(int.MinValue, int.MaxValue)));
    }
}

[TestFixture]
public sealed class CoreIrAdvisoryProductTests
{
    [Test]
    public void NullAndEmptyAndNonemptyJoinsContainConcreteLengths()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var c = f.CreateVariable("c", f.BooleanType);
        var s = f.CreateVariable("s", f.StringType);
        var n = f.CreateVariable("n", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Assign(e, f.CreateOperation(), s, f.Conditional(f.Variable(c), f.Null(f.StringType), f.String("ab")));
        b.Assign(e, f.CreateOperation(), n, f.Length(f.Variable(s)));
        b.Return(e, f.CreateOperation(), f.Variable(n));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [c, s, n]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Outputs[0].Nullness[s], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(result.Outputs[0].Cardinality[s].Length.Contains(0), Is.True);
        Assert.That(result.Outputs[0].Cardinality[s].Length.Contains(2), Is.True);
        foreach (var condition in new[] { false, true })
        { var actual = new IrProgramInterpreter(f).Execute(program, new Dictionary<IrVarId, IrValue> { { c, f.CreateBooleanValue(condition) } }); Assert.That(result.Outputs[0].Values[n].Contains(actual.ReturnValue!.Integer), Is.True); }
        var empty = new IrProgramBuilder(f);
        var first = empty.CreateBlock();
        empty.Assign(first, f.CreateOperation(), s, f.Conditional(f.Variable(c), f.Null(f.StringType), f.String("")));
        empty.Return(first, f.CreateOperation());
        var emptyResult = new CoreIrAdvisoryInterpreter(empty.Build(), [c, s]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(emptyResult.Outputs[0].Cardinality[s].Kind, Is.EqualTo(SequenceCardinalityKind.Empty));
        Assert.That(emptyResult.Outputs[0].Nullness[s], Is.EqualTo(NullnessValue.MaybeNull));
    }
    [Test]
    public void PositiveLengthRefinesNonNullSequenceAndEmptyRetainsNull()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var type = f.GetOrCreateSequenceType(f.IntegerType);
        var c = f.CreateVariable("c", f.BooleanType);
        var s = f.CreateVariable("s", type);
        var n = f.CreateVariable("n", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        var t = b.CreateBlock();
        var no = b.CreateBlock();
        b.Branch(e, f.CreateOperation(), f.Binary(IrBinaryOperator.GreaterThan, f.Length(f.Variable(s)), f.Integer(0)), t, no);
        b.Assign(t, f.CreateOperation(), n, f.Length(f.Variable(s)));
        b.Return(t, f.CreateOperation(), f.Variable(n));
        b.Assign(no, f.CreateOperation(), n, f.Integer(0));
        b.Return(no, f.CreateOperation(), f.Variable(n));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [c, s, n]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Inputs[1].Nullness[s], Is.EqualTo(NullnessValue.NonNull));
        Assert.That(result.Inputs[1].Cardinality[s].Kind, Is.EqualTo(SequenceCardinalityKind.NonEmpty));
        Assert.That(result.Inputs[2].Nullness[s], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(result.Inputs[2].Cardinality[s].Kind, Is.EqualTo(SequenceCardinalityKind.Empty));
        var values = new[] { f.CreateNullValue(type), f.CreateEmptyArrayValue(type), f.CreateSequenceValue(type, new[] { f.CreateIntegerValue(1), f.CreateIntegerValue(2) }) };
        foreach (var value in values)
        { var actual = new IrProgramInterpreter(f).Execute(program, new Dictionary<IrVarId, IrValue> { { s, value } }); var output = result.Outputs[value.Kind == IrValueKind.Sequence && value.Elements.Length > 0 ? 1 : 2]; Assert.That(output.Values[n].Contains(actual.ReturnValue!.Integer), Is.True); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void CallsAndHavocForgetKnownReferenceFacts(bool call)
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var s = f.CreateVariable("s", f.StringType);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Assign(e, f.CreateOperation(), s, f.String("known"));
        if (call)
        { var member = f.GetOrCreateMember(f.CreateIdentity(), f.ObjectType, "Unknown", f.StringType, true, []); b.Call(e, f.CreateOperation(), s, member, null); }
        else
        {
            b.Havoc(e, f.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, s);
        }

        b.Return(e, f.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(b.Build(), [s]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Is.Not.Empty);
        Assert.That(result.Outputs[0].Nullness[s], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(result.Outputs[0].Cardinality[s], Is.EqualTo(SequenceCardinalityDomain.Instance.Top));
    }
    [Test]
    public void ShortCircuitDoesNotObserveUnsupportedOperand()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var unknown = f.CreateVariable("unknown", f.BooleanType);
        var output = f.CreateVariable("output", f.BooleanType);
        var member = f.GetOrCreateMember(f.CreateIdentity(), f.ObjectType, "Unknown", f.BooleanType, true, []);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Assign(e, f.CreateOperation(), unknown, f.Boolean(false));
        b.Assign(e, f.CreateOperation(), output, f.Binary(IrBinaryOperator.AndAlso, f.Variable(unknown), f.PureOpaque(member, null)));
        b.Return(e, f.CreateOperation(), f.Variable(output));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [unknown, output]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Outputs[0].Values[output].SingletonValue, Is.Zero);
        Assert.That(new IrProgramInterpreter(f).Execute(program).ReturnValue!.Boolean, Is.False);
    }
    [Test]
    public void AllocationAndUtf16AndNullConcatMatchConcreteFacts()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var type = f.GetOrCreateSequenceType(f.IntegerType);
        var array = f.CreateVariable("array", type);
        var text = f.CreateVariable("text", f.StringType);
        var n = f.CreateVariable("n", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Allocate(e, f.CreateOperation(), type, array, f.Integer(2));
        b.Assign(e, f.CreateOperation(), text, f.Binary(IrBinaryOperator.StringConcat, f.Null(f.StringType), f.String("\U0001F642")));
        b.Assign(e, f.CreateOperation(), n, f.Binary(IrBinaryOperator.Add, f.Length(f.Variable(text)), f.Length(f.Variable(array))));
        b.Return(e, f.CreateOperation(), f.Variable(n));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [array, text, n]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        var concrete = new IrProgramInterpreter(f).Execute(program);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Outputs[0].Nullness[array], Is.EqualTo(NullnessValue.NonNull));
        Assert.That(result.Outputs[0].Cardinality[array].Length.SingletonValue, Is.EqualTo(2));
        Assert.That(result.Outputs[0].Cardinality[text].Length.SingletonValue, Is.EqualTo(2));
        Assert.That(result.Outputs[0].Values[n].Contains(concrete.ReturnValue!.Integer), Is.True);
    }
    [Test]
    public void NegativeAllocationIsExplicitGap()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var type = f.GetOrCreateSequenceType(f.IntegerType);
        var array = f.CreateVariable("array", type);
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Allocate(e, f.CreateOperation(), type, array, f.Integer(-1));
        b.Return(e, f.CreateOperation());
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [array]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Does.Contain("negative allocation may fault"));
        Assert.That(result.Outputs[0].Nullness[array], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(new IrProgramInterpreter(f).Execute(program).Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
    }
    [Test]
    public void UnsupportedTypesAndForeignVariablesFailAdmission()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var x = f.CreateVariable("x", f.GetOrCreateIntegerType(64, false));
        var b = new IrProgramBuilder(f);
        var e = b.CreateBlock();
        b.Return(e, f.CreateOperation());
        var program = b.Build();
        Assert.That(new CoreIrAdvisoryInterpreter(program, [x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty).Gaps, Does.Contain("unsupported integer type"));
        var other = new IrFactory(IrExecutionSemantics.Total);
        var foreign = other.CreateVariable("other", other.IntegerType);
        Assert.That(new CoreIrAdvisoryInterpreter(program, [foreign]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty).Gaps, Does.Contain("variable ownership"));
    }
}
[TestFixture]
public sealed class CoreIrAdvisoryAdmissionLimitTests
{
    [TestCase("blocks")]
    [TestCase("instructions")]
    [TestCase("variables")]
    public void OversizedInputReportsBoundedAdmissionGap(string kind)
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var variables = Enumerable.Range(0, kind == "variables" ? 4097 : 1).Select(i => f.CreateVariable("x" + i, f.IntegerType)).ToImmutableArray();
        var b = new IrProgramBuilder(f);
        var entry = b.CreateBlock();
        if (kind == "instructions")
        {
            for (var i = 0; i < 4096; i++)
            {
                b.Assign(entry, f.CreateOperation(), variables[0], f.Integer(i));
            }
        }

        b.Return(entry, f.CreateOperation());
        if (kind == "blocks")
        {
            for (var i = 0; i < 4096; i++)
            { var next = b.CreateBlock(); b.Return(next, f.CreateOperation()); }
        }

        var result = new CoreIrAdvisoryInterpreter(b.Build(), variables).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Iterations, Is.Zero);
        Assert.That(result.Gaps, Does.Contain(kind == "variables" ? "variable budget/ownership" : "admission/graph budget"));
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Outputs, Is.Empty);
        Assert.That(result.TryGetOutput(0, out _), Is.False);
        Assert.Throws<InvalidOperationException>((Action)(() => result.RequireOutput(0)));
    }
}

[TestFixture]
public sealed class CoreIrAdvisoryResourceTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void NullableCfgMergeContainsNullAndNonemptyConcreteResults(bool sequence)
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var type = sequence ? f.GetOrCreateSequenceType(f.IntegerType) : f.StringType;
        var c = f.CreateVariable("c", f.BooleanType);
        var value = f.CreateVariable("value", type);
        var n = f.CreateVariable("n", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var entry = b.CreateBlock();
        var left = b.CreateBlock();
        var right = b.CreateBlock();
        var exit = b.CreateBlock();
        b.Branch(entry, f.CreateOperation(), f.Variable(c), left, right);
        b.Assign(left, f.CreateOperation(), value, f.Null(type));
        b.Goto(left, f.CreateOperation(), exit);
        if (sequence)
        {
            b.Allocate(right, f.CreateOperation(), type, value, f.Integer(2));
        }
        else
        {
            b.Assign(right, f.CreateOperation(), value, f.String("ab"));
        }

        b.Goto(right, f.CreateOperation(), exit);
        b.Assign(exit, f.CreateOperation(), n, f.Length(f.Variable(value)));
        b.Return(exit, f.CreateOperation(), f.Variable(n));
        var program = b.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [c, value, n]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        var merged = result.RequireOutput(3);
        Assert.That(merged.Nullness[value], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(merged.Cardinality[value].Length.Contains(0), Is.True);
        Assert.That(merged.Cardinality[value].Length.Contains(2), Is.True);
        foreach (var condition in new[] { false, true })
        { var concrete = new IrProgramInterpreter(f).Execute(program, new Dictionary<IrVarId, IrValue> { { c, f.CreateBooleanValue(condition) } }); Assert.That(merged.Values[n].Contains(concrete.ReturnValue!.Integer), Is.True); }
    }
    [Test]
    public void AggregateTermWorkStopsBeforePublishingPartialStates()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var x = f.CreateVariable("x", f.IntegerType);
        var b = new IrProgramBuilder(f);
        var entry = b.CreateBlock();
        for (var i = 0; i < 3; i++)
        {
            b.Assign(entry, f.CreateOperation(), x, f.Binary(IrBinaryOperator.Add, f.Variable(x), f.Integer(1)));
        }

        b.Return(entry, f.CreateOperation(), f.Variable(x));
        var result = new CoreIrAdvisoryInterpreter(b.Build(), [x]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(x, IntervalValue.Constant(0)), maximumTermWork: 2);
        AssertRejected(result, "aggregate term work budget");
    }
    [Test]
    public void SyntheticEdgeBudgetRejectsBeforeFixedPoint()
    {
        var f = new IrFactory(IrExecutionSemantics.Total);
        var c = f.CreateVariable("c", f.BooleanType);
        var b = new IrProgramBuilder(f);
        var entry = b.CreateBlock();
        var exit = b.CreateBlock();
        b.Branch(entry, f.CreateOperation(), f.Variable(c), exit, exit);
        b.Return(exit, f.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(b.Build(), [c]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, maximumSyntheticEdges: 1);
        AssertRejected(result, "synthetic edge budget");
    }
    private static void AssertRejected(CoreIrAdvisoryResult result, string gap)
    {
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps, Does.Contain(gap));
        Assert.That(result.Inputs, Is.Empty);
        Assert.That(result.Outputs, Is.Empty);
        Assert.That(result.TryGetOutput(0, out _), Is.False);
        Assert.Throws<InvalidOperationException>((Action)(() => result.RequireOutput(0)));
    }
}

[TestFixture]
public sealed class CoreIrAdvisoryExceptionalAdmissionTests
{
    [Test]
    public void ThrowToHandlerDoesNotExposeFalseUnreachableState()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var handler = builder.CreateBlock();
        builder.Throw(entry, factory.CreateOperation(), IrExceptionKind.Overflow, handler);
        builder.Assign(handler, factory.CreateOperation(), value, factory.Integer(7));
        builder.Return(handler, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();
        var concrete = new IrProgramInterpreter(factory).Execute(program);
        Assert.That(concrete.ReturnValue!.Integer, Is.EqualTo(7));
        var result = new CoreIrAdvisoryInterpreter(program, [value]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps, Does.Contain("unsupported exceptional control flow"));
        Assert.That(result.Outputs, Is.Empty);
        Assert.That(result.TryGetOutput(handler.Value, out _), Is.False);
    }
}
[TestFixture]
public sealed class CoreIrAdvisoryInitialFactTests
{
    [TestCase(false, -1L)]
    [TestCase(false, 256L)]
    [TestCase(true, -1L)]
    [TestCase(true, 2L)]
    public void OutOfTypeInitialFactsAreRejected(bool boolean, long input)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("input", boolean ? factory.BooleanType : factory.GetOrCreateIntegerType(8, false));
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [variable]).Run(
            ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(variable, IntervalValue.Constant(input)));
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps, Does.Contain("invalid initial scalar facts"));
        Assert.That(result.Outputs, Is.Empty);
        Assert.That(result.TryGetOutput(0, out _), Is.False);
    }

    [TestCase(false, 0L)]
    [TestCase(false, 255L)]
    [TestCase(true, 0L)]
    [TestCase(true, 1L)]
    public void TypedBoundaryInitialFactsRemainAvailable(bool boolean, long input)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("input", boolean ? factory.BooleanType : factory.GetOrCreateIntegerType(8, false));
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [variable]).Run(
            ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(variable, IntervalValue.Constant(input)));
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[variable].Contains(input), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BottomAndReferenceScalarFactsAreRejected(bool reference)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("input", reference ? factory.StringType : factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [variable]).Run(
            ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(variable,
                reference ? IntervalValue.Constant(1) : IntervalValue.Bottom));
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps, Does.Contain("invalid initial scalar facts"));
        Assert.That(result.Outputs, Is.Empty);
    }
}
[TestFixture]
public sealed class CoreIrAdvisorySourceAdmissionTests
{
    [TestCase("return value + 1;", -1L, 3L)]
    [TestCase("if (value < 0) return 0; return value;", -2L, 2L)]
    public void DetachedFrontendExceptionalExitDoesNotRejectOrdinarySource(string body, long lower, long upper)
    {
        var compilation = AnalyzerTestHost.CreateCompilation("static class Subject { public static int Target(int value) { " + body + " } }", []);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var context = new SharpProof.Frontend.TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total),
            (Microsoft.CodeAnalysis.IMethodSymbol)Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, syntax)!);
        var graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!;
        var lowering = new SharpProof.Frontend.RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.Program.Blocks.Count(block => block.Terminator is IrExceptionalExitInstruction), Is.EqualTo(1));
        var variables = context.Parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Current, parameter.PreState })
            .Concat(lowering.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>().Select(assign => assign.Target))
            .Append(context.Result!.Value).Distinct().ToImmutableArray();
        var result = new CoreIrAdvisoryInterpreter(lowering.Program, variables).Run(
            ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(context.Parameters[0].Entry, IntervalValue.Range(-2, 2)));
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        var returnBlocks = lowering.Program.Blocks.Where(block => block.Terminator is IrReturnInstruction && result.RequireOutput(block.Id.Value).Reachable);
        var abstractResult = returnBlocks.Aggregate(IntervalValue.Bottom,
            (combined, block) => IntervalDomain.Instance.Join(combined, result.RequireOutput(block.Id.Value).Values[context.Result.Value]));
        var expected = IntervalValue.Range(lower, upper);
        Assert.That(abstractResult, Is.EqualTo(expected));
        using var stream = new MemoryStream();
        Assert.That(compilation.Emit(stream).Success, Is.True);
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("AdvisorySourceProbe", isCollectible: true);
        try
        {
            stream.Position = 0;
            var target = loadContext.LoadFromStream(stream).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in new[] { -2, -1, 0, 1, 2 })
            {
                var concrete = new IrProgramInterpreter(context.Factory).Execute(lowering.Program,
                    new Dictionary<IrVarId, IrValue> { { context.Parameters[0].Entry, context.Factory.CreateIntegerValue(input) } });
                var compiled = (int)target.Invoke(null, [input])!;
                Assert.That(concrete.ReturnValue!.Integer, Is.EqualTo(compiled));
                Assert.That(abstractResult.Contains(concrete.ReturnValue.Integer), Is.True);
                Assert.That(abstractResult.Contains(compiled), Is.True);
            }
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Test]
    public void ReachableExceptionalExitStillRejectsTheGraph()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.ExceptionalExit(entry, factory.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), []).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps, Does.Contain("unsupported exceptional control flow"));
        Assert.That(result.TryGetOutput(0, out _), Is.False);
        Assert.That(result.Outputs, Is.Empty);
    }
}