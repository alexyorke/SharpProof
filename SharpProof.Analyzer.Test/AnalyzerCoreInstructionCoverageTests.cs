using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreInstructionCoverageTests
{
    [Test]
    public void AnAssertionRetainsItsConcreteResultWithAnExplicitUnsupportedInstructionGap()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(3));
        builder.Assert(entry, factory.CreateOperation(), factory.Boolean(true));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();
        var concrete = new IrProgramInterpreter(factory).Execute(program).ReturnValue!.Integer;
        var result = new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(concrete, Is.EqualTo(3));
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("unsupported instruction"));
        Assert.That(result.RequireOutput(0).HasGap, Is.True);
        var domain = new CoreIrAdvisoryDomain(factory, [value]);
        Assert.That(domain.Get(result.RequireOutput(0), value).Contains(concrete), Is.True);
    }

    [Test]
    public void AnAssumptionRetainsOnlyItsAdmittedConcreteBooleanInput()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assume(entry, factory.CreateOperation(), factory.Variable(flag));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(flag));
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [flag])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        var concrete = new IrProgramInterpreter(factory).Execute(program,
            new Dictionary<IrVarId, IrValue> { { flag, factory.CreateBooleanValue(true) } }).ReturnValue!.Boolean;
        Assert.That(concrete, Is.True);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[flag].SingletonValue, Is.EqualTo(1));
    }

    [Test]
    public void ADeepIntegerExpressionRetainsItsConcreteResultWithAnExplicitDepthGap()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var input = factory.CreateVariable("input", factory.IntegerType);
        var output = factory.CreateVariable("output", factory.IntegerType);
        IrTerm expression = factory.Variable(input);
        for (var index = 0; index < 130; index++)
        {
            expression = factory.Binary(IrBinaryOperator.Add, expression, factory.Integer(1));
        }
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), input, factory.Integer(0));
        builder.Assign(entry, factory.CreateOperation(), output, expression);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(output));
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [input, output])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        var concrete = new IrProgramInterpreter(factory).Execute(program).ReturnValue!.Integer;
        Assert.That(concrete, Is.EqualTo(130));
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("term depth"));
        Assert.That(result.RequireOutput(0).HasGap, Is.True);
        Assert.That(result.RequireOutput(0).Values[output].Contains(concrete), Is.True);
    }

    [Test]
    public void ADeepReferenceExpressionRemainsUnknownWithAnExplicitDepthGap()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var output = factory.CreateVariable("output", factory.StringType);
        IrTerm expression = factory.Null(factory.StringType);
        for (var index = 0; index < 130; index++)
        {
            expression = factory.Conditional(factory.Variable(flag), factory.String("value"), expression);
        }
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), output, expression);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(output));
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [flag, output])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("term depth"));
        Assert.That(result.RequireOutput(0).HasGap, Is.True);
        Assert.That(result.RequireOutput(0).Nullness[output], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(result.RequireOutput(0).Cardinality[output].Length.Contains(0), Is.True);
        Assert.That(result.RequireOutput(0).Cardinality[output].Length.Contains(5), Is.True);
        foreach (var condition in new[] { false, true })
        {
            var concrete = new IrProgramInterpreter(factory).Execute(program,
                new Dictionary<IrVarId, IrValue> { { flag, factory.CreateBooleanValue(condition) } }).ReturnValue!;
            Assert.That(concrete.Kind, Is.EqualTo(condition ? IrValueKind.String : IrValueKind.Null));
            if (condition)
            {
                Assert.That(result.RequireOutput(0).Cardinality[output].Length.Contains(concrete.String.Length), Is.True);
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void KnownConditionalSelectsTheConcreteIntegerResult(bool condition)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), flag, factory.Boolean(condition));
        builder.Assign(entry, factory.CreateOperation(), value,
            factory.Conditional(factory.Variable(flag), factory.Integer(9), factory.Integer(-4)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [flag, value]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        var concrete = new IrProgramInterpreter(factory).Execute(program).ReturnValue!.Integer;
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[value].SingletonValue, Is.EqualTo(concrete));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void KnownConditionalSelectsTheReferenceNullness(bool condition)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var value = factory.CreateVariable("value", factory.StringType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), flag, factory.Boolean(condition));
        builder.Assign(entry, factory.CreateOperation(), value,
            factory.Conditional(factory.Variable(flag), factory.String("value"), factory.Null(factory.StringType)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [flag, value]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Nullness[value], Is.EqualTo(condition ? NullnessValue.NonNull : NullnessValue.Null));
    }

    [Test]
    public void NegatedBranchRefinesBothConcreteBooleanInputs()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var whenTrue = builder.CreateBlock();
        var whenFalse = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Unary(IrUnaryOperator.Not, factory.Variable(flag)), whenTrue, whenFalse);
        builder.Return(whenTrue, factory.CreateOperation(), factory.Variable(flag));
        builder.Return(whenFalse, factory.CreateOperation(), factory.Variable(flag));
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [flag]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(1).Values[flag].SingletonValue, Is.Zero);
        Assert.That(result.RequireOutput(2).Values[flag].SingletonValue, Is.EqualTo(1));
        foreach (var input in new[] { false, true })
        {
            var concrete = new IrProgramInterpreter(factory).Execute(program,
                new Dictionary<IrVarId, IrValue> { [flag] = factory.CreateBooleanValue(input) });
            Assert.That(concrete.ReturnValue!.Boolean, Is.EqualTo(input));
        }
    }

    [TestCase("call", "unsupported call")]
    [TestCase("load", "unsupported load")]
    [TestCase("havoc", "memory havoc")]
    public void UnmodeledInstructionsForgetAnEarlierConstant(string instruction, string gap)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Value", factory.IntegerType, true, []);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(7));
        switch (instruction)
        {
            case "call":
                builder.Call(entry, factory.CreateOperation(), value, member, null);
                break;
            case "load":
                builder.Load(entry, factory.CreateOperation(), value, builder.MemberLocation(member, null));
                break;
            case "havoc":
                builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.VariablesAndMemory, value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(instruction));
        }
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [value]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain(gap));
        var state = result.RequireOutput(0);
        var domain = new CoreIrAdvisoryDomain(factory, [value]);
        Assert.That(state.HasGap, Is.True);
        Assert.That(domain.Get(state, value).LowerBound, Is.EqualTo(int.MinValue));
        Assert.That(domain.Get(state, value).UpperBound, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void AnOpaqueStringValueRetainsAnExplicitReferenceGap()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.StringType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Text", factory.StringType, true, []);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), value, factory.PureOpaque(member, null));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [value]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("unsupported reference term"));
        Assert.That(result.RequireOutput(0).Nullness[value], Is.EqualTo(NullnessValue.MaybeNull));
        Assert.That(result.RequireOutput(0).Cardinality[value], Is.EqualTo(SequenceCardinalityDomain.Instance.Top));
    }
}
