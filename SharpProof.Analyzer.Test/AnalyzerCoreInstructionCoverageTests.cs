using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreInstructionCoverageTests
{
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
