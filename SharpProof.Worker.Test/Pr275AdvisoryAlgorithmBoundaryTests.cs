using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Dataflow;
using SharpProof.Ir;
using CoreScalarIntervalTransfer = SharpProof.Advisory.Core.CoreIrScalarIntervalTransfer;


namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275AdvisoryAlgorithmBoundaryTests
{
    [Test]
    public void InterpreterRejectsNullProgramAndDefaultVariableList()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();

        var nullProgram = Assert.Throws<ArgumentNullException>(
            new Action(() => { _ = new CoreIrAdvisoryInterpreter(null!, ImmutableArray<IrVarId>.Empty); }));
        Assert.That(nullProgram!.ParamName, Is.EqualTo("program"));

        var defaultVariables = Assert.Throws<ArgumentException>(
            new Action(() => { _ = new CoreIrAdvisoryInterpreter(program, default); }));
        Assert.That(defaultVariables!.ParamName, Is.EqualTo("variables"));

        var accepted = new CoreIrAdvisoryInterpreter(program, ImmutableArray<IrVarId>.Empty)
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, token: TestContext.CurrentContext.CancellationToken);
        Assert.That(accepted.Accepted, Is.True);
        Assert.That(accepted.Gaps, Is.Empty);
    }

    [Test]
    public void ScalarTransferUsesActualIntegerTypesForInvalidAndBottomInputs()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integerType = factory.GetTypeInfo(factory.IntegerType);
        var zero = IntervalValue.Constant(0);
        var one = IntervalValue.Constant(1);
        var bottom = IntervalValue.Bottom;

        Assert.That(
            CoreScalarIntervalTransfer.Binary(IrBinaryOperator.Add, integerType, zero, zero),
            Is.EqualTo(zero));
        var unsupported = Assert.Throws<ArgumentOutOfRangeException>(
            new Action(() => { _ = CoreScalarIntervalTransfer.Binary(IrBinaryOperator.Equal, integerType, zero, zero); }));
        Assert.That(unsupported!.ParamName, Is.EqualTo("operation"));

        Assert.That(CoreScalarIntervalTransfer.Negate(integerType, bottom), Is.EqualTo(bottom));
        Assert.That(CoreScalarIntervalTransfer.Negate(integerType, one), Is.EqualTo(IntervalValue.Constant(-1)));
        Assert.That(CoreScalarIntervalTransfer.Cast(integerType, integerType, bottom), Is.EqualTo(bottom));
        Assert.That(CoreScalarIntervalTransfer.Cast(integerType, integerType, one), Is.EqualTo(one));
    }

    [Test]
    public void MemoryHavocReportsItsGapAndPreservesScalarFacts()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Memory, IrHavocOrigin.Input);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();

        var result = new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Constant(7)), token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("memory havoc"));
        var output = result.RequireOutput(0);
        Assert.That(output.Values[value], Is.EqualTo(IntervalValue.Constant(7)));
        Assert.That(output.HasGap, Is.True);
    }

    [Test]
    public void AssumeOfNegatedComparisonRefinesTheActualInputInterval()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var lessThanFive = factory.Binary(
            IrBinaryOperator.LessThan, factory.Variable(value), factory.Integer(5));
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assume(entry, factory.CreateOperation(),
            factory.Unary(IrUnaryOperator.Not, lessThanFive));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();

        var result = new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Range(0, 10)), token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(
            result.RequireOutput(0).Values[value],
            Is.EqualTo(IntervalValue.Range(5, 10)));
    }

    [Test]
    public void AssumeFalseProducesAnUnreachableOutputThroughRun()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assume(entry, factory.CreateOperation(), factory.Boolean(false));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();

        var result = new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Constant(3)), token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        var output = result.RequireOutput(0);
        Assert.That(output.Reachable, Is.False);
        Assert.That(output.Values.ContainsKey(value), Is.False);
    }

    [Test]
    public void UnsupportedLoadReportsGapAndForgetsOnlyItsDestination()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence = factory.CreateVariable("sequence", sequenceType);
        var loaded = factory.CreateVariable("loaded", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var location = builder.SequenceLocation(factory.Variable(sequence), factory.Integer(0));
        builder.Load(entry, factory.CreateOperation(), loaded, location);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(loaded));
        var program = builder.Build();

        var result = new CoreIrAdvisoryInterpreter(program, [sequence, loaded])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(loaded, IntervalValue.Constant(9)), token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("unsupported load"));
        Assert.That(
            result.RequireOutput(0).Values[loaded],
            Is.EqualTo(IntervalValue.Range(int.MinValue, int.MaxValue)));
    }

    [Test]
    public void UnsupportedAssertionInstructionReturnsARealGapTopState()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(7));
        builder.Assert(entry, factory.CreateOperation(), factory.Boolean(true));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();

        var result = new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("unsupported instruction"));
        var output = result.RequireOutput(0);
        Assert.That(output.Reachable, Is.True);
        Assert.That(output.HasGap, Is.True);
        Assert.That(output.Values.ContainsKey(value), Is.False);
    }

    [Test]
    public void SingletonConditionalAndStringEqualityUseCurrentProgramFacts()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        var scalar = factory.CreateVariable("scalar", factory.IntegerType);
        var conditionalText = factory.CreateVariable("conditionalText", factory.StringType);
        var left = factory.CreateVariable("left", factory.StringType);
        var right = factory.CreateVariable("right", factory.StringType);
        var equal = factory.CreateVariable("equal", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();

        builder.Assign(entry, factory.CreateOperation(), scalar,
            factory.Conditional(factory.Variable(condition), factory.Integer(7), factory.Integer(11)));
        builder.Assign(entry, factory.CreateOperation(), conditionalText,
            factory.Conditional(factory.Variable(condition), factory.Null(factory.StringType), factory.String("fallback")));
        builder.Assign(entry, factory.CreateOperation(), left, factory.Null(factory.StringType));
        builder.Assign(entry, factory.CreateOperation(), right, factory.Null(factory.StringType));
        builder.Assign(entry, factory.CreateOperation(), equal,
            factory.Binary(IrBinaryOperator.StringEquals, factory.Variable(left), factory.Variable(right)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(scalar));
        var program = builder.Build();

        var result = new CoreIrAdvisoryInterpreter(program, [condition, scalar, conditionalText, left, right, equal])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(condition, IntervalValue.Constant(1)), token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        var output = result.RequireOutput(0);
        Assert.That(output.Values[scalar], Is.EqualTo(IntervalValue.Constant(7)));
        Assert.That(output.Nullness[conditionalText], Is.EqualTo(NullnessValue.Null));
        Assert.That(output.Values[equal], Is.EqualTo(IntervalValue.Constant(1)));
    }

    [Test]
    public void DeepTermsAndOpaqueReferenceUseConservativeBoundaries()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        var input = factory.CreateVariable("input", factory.IntegerType);
        var deepValue = factory.CreateVariable("deepValue", factory.IntegerType);
        var deepText = factory.CreateVariable("deepText", factory.StringType);
        var opaqueText = factory.CreateVariable("opaqueText", factory.StringType);

        IrTerm numeric = factory.Variable(input);
        for (var depth = 0; depth < 130; depth++)
        {
            numeric = factory.Binary(IrBinaryOperator.Add, numeric, factory.Integer(1));
        }

        IrTerm nestedText = factory.Null(factory.StringType);
        for (var depth = 0; depth < 130; depth++)
        {
            nestedText = factory.Conditional(
                factory.Variable(condition), nestedText, factory.String("fallback"));
        }

        var opaqueMember = factory.GetOrCreateMember(
            factory.CreateIdentity(), factory.ObjectType, "OpaqueText", factory.StringType, true, []);
        var opaqueValue = factory.PureOpaque(opaqueMember, null);

        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), deepValue, numeric);
        builder.Assign(entry, factory.CreateOperation(), deepText, nestedText);
        builder.Assign(entry, factory.CreateOperation(), opaqueText, opaqueValue);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(deepValue));
        var program = builder.Build();

        var result = new CoreIrAdvisoryInterpreter(program, [condition, input, deepValue, deepText, opaqueText])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty
                .Add(condition, IntervalValue.Constant(1))
                .Add(input, IntervalValue.Constant(0)), token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("term depth"));
        Assert.That(result.Gaps, Does.Contain("unsupported reference term"));
        var output = result.RequireOutput(0);
        Assert.That(
            output.Values[deepValue],
            Is.EqualTo(IntervalValue.Range(int.MinValue, int.MaxValue)));
        Assert.That(output.Nullness[deepText], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(output.Nullness[opaqueText], Is.EqualTo(NullnessValue.MaybeNull));
    }

    [Test]
    public void OwnedComparisonMarkersResolveDefiniteOutcomesFromIntervals()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var lower = factory.CreateVariable("lower", factory.IntegerType);
        var upper = factory.CreateVariable("upper", factory.IntegerType);
        var equal = factory.CreateVariable("equal", factory.BooleanType);
        var notEqual = factory.CreateVariable("notEqual", factory.BooleanType);
        var greater = factory.CreateVariable("greater", factory.BooleanType);
        var less = factory.CreateVariable("less", factory.BooleanType);
        var greaterOrEqual = factory.CreateVariable("greaterOrEqual", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();

        builder.Assign(entry, factory.CreateOperation(), equal,
            factory.Binary(IrBinaryOperator.Equal, factory.Variable(lower), factory.Variable(upper)));
        builder.Assign(entry, factory.CreateOperation(), notEqual,
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(lower), factory.Variable(upper)));
        builder.Assign(entry, factory.CreateOperation(), greater,
            factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(lower), factory.Variable(upper)));
        builder.Assign(entry, factory.CreateOperation(), less,
            factory.Binary(IrBinaryOperator.LessThan, factory.Variable(lower), factory.Variable(upper)));
        builder.Assign(entry, factory.CreateOperation(), greaterOrEqual,
            factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(lower), factory.Variable(upper)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(equal));
        var program = builder.Build();
        var markers = program.Blocks
            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrAssignInstruction>()
            .ToImmutableArray();

        var result = new CoreIrAdvisoryInterpreter(program, [lower, upper, equal, notEqual, greater, less, greaterOrEqual])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty
                .Add(lower, IntervalValue.Range(0, 1))
                .Add(upper, IntervalValue.Range(3, 4)),
                markers: markers, token: TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        var output = result.RequireOutput(0);
        Assert.That(output.Values[equal], Is.EqualTo(IntervalValue.Constant(0)));
        Assert.That(output.Values[notEqual], Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(output.Values[greater], Is.EqualTo(IntervalValue.Constant(0)));
        Assert.That(output.Values[less], Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(output.Values[greaterOrEqual], Is.EqualTo(IntervalValue.Constant(0)));
    }
}
