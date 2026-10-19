using System.Collections.Generic;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrInputSnapshotTests
{
    [Test]
    public void ReadObserverCannotChangeTheCapturedVariableEnvironment()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.IntegerType);
        var second = factory.CreateVariable("second", factory.IntegerType);
        // The mutable input dictionary is the ownership boundary under test.
        var variables = new Dictionary<IrVarId, IrValue>
        {
            [first] = factory.CreateIntegerValue(10),
            [second] = factory.CreateIntegerValue(20)
        };
        var term = factory.Binary(IrBinaryOperator.Add, factory.Variable(first), factory.Variable(second));
        var result = new IrInterpreter(factory).Evaluate(term, variables, variable =>
        {
            if (variable.IsDefault)
            { throw new ArgumentException("Value must be initialized.", nameof(variable)); }
            if (variable == first)
            { variables[second] = factory.CreateIntegerValue(99); }
        }, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(30));
        Assert.That(variables[second].Integer, Is.EqualTo(99));
    }

    [Test]
    public void ReadObserverCannotChangeReadOnlyDictionaryFallbackSnapshot()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.IntegerType);
        var second = factory.CreateVariable("second", factory.IntegerType);
        // Mutating this backing input during observation is the subject under test.
        var backing = new Dictionary<IrVarId, IrValue>
        {
            [first] = factory.CreateIntegerValue(10),
            [second] = factory.CreateIntegerValue(20)
        };
        var variables = new System.Collections.ObjectModel.ReadOnlyDictionary<IrVarId, IrValue>(backing);
        var term = factory.Binary(IrBinaryOperator.Add, factory.Variable(first), factory.Variable(second));
        var result = new IrInterpreter(factory).Evaluate(term, variables, variable =>
        {
            if (variable.IsDefault)
            { throw new ArgumentException("Value must be initialized.", nameof(variable)); }
            if (variable == first)
            { backing[second] = factory.CreateIntegerValue(99); }
        }, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(30));
        Assert.That(backing[second].Integer, Is.EqualTo(99));
    }

    [Test]
    public void CapturedDictionaryPreservesItsKeyComparer()
    {
        var otherFactory = new IrFactory(IrExecutionSemantics.Total);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var other = otherFactory.CreateVariable("other", otherFactory.IntegerType);
        var current = factory.CreateVariable("current", factory.IntegerType);
        var comparer = EqualityComparer<IrVarId>.Create(
            (left, right) => left.Value == right.Value,
            variable /* SPINT001: all values valid - hashing supports initialized and default identifiers */ => variable.Value.GetHashCode());
        // A mutable dictionary with custom equality is the input contract under test.
        var variables = new Dictionary<IrVarId, IrValue>(comparer)
        {
            [other] = factory.CreateIntegerValue(17)
        };

        var result = new IrInterpreter(factory).Evaluate(factory.Variable(current), variables,
            TestContext.CurrentContext.CancellationToken);

        Assert.That(other, Is.Not.EqualTo(current));
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(17));
    }
}
