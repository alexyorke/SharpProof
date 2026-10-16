using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275AdvisoryRunOutputDomainTests
{
    [Test]
    public void WidenPreservesReachableIdentityBesideActualUnreachableOutput()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var reachableProgram = Build(factory, value, false);
        var unreachableProgram = Build(factory, value, true);
        var reachableResult = new CoreIrAdvisoryInterpreter(reachableProgram, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Constant(3)));
        var unreachableResult = new CoreIrAdvisoryInterpreter(unreachableProgram, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Constant(3)));
        Assert.That(reachableResult.Accepted, Is.True);
        Assert.That(unreachableResult.Accepted, Is.True);
        Assert.That(reachableResult.Gaps, Is.Empty);
        Assert.That(unreachableResult.Gaps, Is.Empty);
        var reachable = reachableResult.RequireOutput(0);
        var unreachable = unreachableResult.RequireOutput(0);
        Assert.That(reachable.Reachable, Is.True);
        Assert.That(reachable.Values[value], Is.EqualTo(IntervalValue.Constant(3)));
        Assert.That(unreachable.Reachable, Is.False);
        Assert.That(unreachable.Values, Is.Empty);

        var domain = new CoreIrAdvisoryDomain(factory, [value]);
        Assert.That(domain.Widen(unreachable, reachable), Is.SameAs(reachable));
        Assert.That(domain.Widen(reachable, unreachable), Is.SameAs(reachable));
        Assert.That(domain.Join(unreachable, reachable), Is.SameAs(reachable));
        Assert.That(domain.Join(reachable, unreachable), Is.SameAs(reachable));
        var ordinary = domain.Widen(reachable, reachable);
        Assert.That(ordinary.Reachable, Is.True);
        Assert.That(ordinary.HasGap, Is.False);
        Assert.That(ordinary.Values[value], Is.EqualTo(IntervalValue.Constant(3)));
    }

    [Test]
    public void HavocUsesOnlyTheReachabilityOfActualRunOutputs()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var reachableResult = new CoreIrAdvisoryInterpreter(Build(factory, value, false), [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Constant(3)));
        var unreachableResult = new CoreIrAdvisoryInterpreter(Build(factory, value, true), [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Constant(3)));
        Assert.That(reachableResult.Accepted, Is.True);
        Assert.That(unreachableResult.Accepted, Is.True);
        Assert.That(reachableResult.Gaps, Is.Empty);
        Assert.That(unreachableResult.Gaps, Is.Empty);
        var reachable = reachableResult.RequireOutput(0);
        var unreachable = unreachableResult.RequireOutput(0);
        Assert.That(reachable.Reachable, Is.True);
        Assert.That(reachable.Values[value], Is.EqualTo(IntervalValue.Constant(3)));
        Assert.That(unreachable.Reachable, Is.False);

        var domain = new CoreIrAdvisoryDomain(factory, [value]);
        var reachableHavoc = domain.Havoc(reachable);
        var unreachableHavoc = domain.Havoc(unreachable);
        Assert.That(reachableHavoc.Reachable, Is.True);
        Assert.That(reachableHavoc.HasGap, Is.True);
        Assert.That(reachableHavoc.Values, Is.Empty);
        Assert.That(unreachableHavoc.Reachable, Is.False);
        Assert.That(unreachableHavoc.HasGap, Is.False);
        Assert.That(unreachableHavoc.Values, Is.Empty);
    }

    [Test]
    public void SetReferenceAcceptsLatticeBottomBesideActualReferenceOutputs()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.CreateVariable("text", factory.StringType);
        var nonNullBuilder = new IrProgramBuilder(factory);
        var nonNullEntry = nonNullBuilder.CreateBlock();
        nonNullBuilder.Assign(nonNullEntry, factory.CreateOperation(), text, factory.String("owned"));
        nonNullBuilder.Return(nonNullEntry, factory.CreateOperation(), factory.Variable(text));
        var nonNullResult = new CoreIrAdvisoryInterpreter(nonNullBuilder.Build(), [text])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        var nullBuilder = new IrProgramBuilder(factory);
        var nullEntry = nullBuilder.CreateBlock();
        nullBuilder.Assign(nullEntry, factory.CreateOperation(), text, factory.Null(factory.StringType));
        nullBuilder.Return(nullEntry, factory.CreateOperation(), factory.Variable(text));
        var nullResult = new CoreIrAdvisoryInterpreter(nullBuilder.Build(), [text])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(nonNullResult.Accepted, Is.True);
        Assert.That(nullResult.Accepted, Is.True);
        Assert.That(nonNullResult.Gaps, Is.Empty);
        Assert.That(nullResult.Gaps, Is.Empty);
        var nonNull = nonNullResult.RequireOutput(0);
        var isNull = nullResult.RequireOutput(0);
        Assert.That(nonNull.Reachable, Is.True);
        Assert.That(nonNull.Nullness[text], Is.EqualTo(NullnessValue.NonNull));
        Assert.That(nonNull.Cardinality[text].Length, Is.EqualTo(IntervalValue.Constant(5)));
        Assert.That(isNull.Reachable, Is.True);
        Assert.That(isNull.Nullness[text], Is.EqualTo(NullnessValue.Null));
        Assert.That(isNull.Cardinality[text], Is.EqualTo(SequenceCardinalityDomain.Instance.Empty));

        var domain = new CoreIrAdvisoryDomain(factory, [text]);
        var nullnessBottom = domain.SetReference(nonNull, text,
            NullnessValue.Bottom, SequenceCardinalityDomain.Instance.Empty);
        var cardinalityBottom = domain.SetReference(nonNull, text,
            NullnessValue.NonNull, SequenceCardinalityDomain.Instance.Bottom);
        Assert.That(nullnessBottom.Reachable, Is.False);
        Assert.That(nullnessBottom.Values, Is.Empty);
        Assert.That(nullnessBottom.Nullness, Is.Empty);
        Assert.That(nullnessBottom.Cardinality, Is.Empty);
        Assert.That(cardinalityBottom.Reachable, Is.False);
        Assert.That(cardinalityBottom.Values, Is.Empty);
        Assert.That(cardinalityBottom.Nullness, Is.Empty);
        Assert.That(cardinalityBottom.Cardinality, Is.Empty);

        var nonNullControl = domain.SetReference(nonNull, text,
            nonNull.Nullness[text], nonNull.Cardinality[text]);
        var nullControl = domain.SetReference(isNull, text,
            isNull.Nullness[text], isNull.Cardinality[text]);
        Assert.That(nonNullControl.Reachable, Is.True);
        Assert.That(nonNullControl.HasGap, Is.False);
        Assert.That(nonNullControl.Nullness[text], Is.EqualTo(nonNull.Nullness[text]));
        Assert.That(nonNullControl.Cardinality[text], Is.EqualTo(nonNull.Cardinality[text]));
        Assert.That(nullControl.Reachable, Is.True);
        Assert.That(nullControl.HasGap, Is.False);
        Assert.That(nullControl.Nullness[text], Is.EqualTo(isNull.Nullness[text]));
        Assert.That(nullControl.Cardinality[text], Is.EqualTo(isNull.Cardinality[text]));
    }
    private static IrProgram Build(IrFactory factory, IrVarId value, bool assumeFalse)
    {
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (assumeFalse)
        {
            builder.Assume(entry, factory.CreateOperation(), factory.Boolean(false));
        }
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        return builder.Build();
    }
}
