using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275SemanticGuardCoverageTests
{
    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void GuardAppliesTheConsequenceOnlyWhenItsConditionHolds(
        bool conditionValue, bool consequenceValue, bool expected)
    {
        var factory = new IrFactory();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        var consequence = factory.CreateVariable("consequence", factory.BooleanType);
        var term = IrSemanticTerms.Guard(factory,
            factory.Variable(condition), factory.Variable(consequence));
        var result = new IrInterpreter(factory).Evaluate(term,
            new Dictionary<IrVarId, IrValue>
            {
                [condition] = factory.CreateBooleanValue(conditionValue),
                [consequence] = factory.CreateBooleanValue(consequenceValue)
            });

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Boolean, Is.EqualTo(expected));
        Assert.That(IrTermAnalysis.CollectVariables(term),
            Is.EquivalentTo(new[] { condition, consequence }));
    }

    [TestCase("factory")]
    [TestCase("condition")]
    [TestCase("consequence")]
    public void GuardNamesItsMissingRequiredArgument(string missing)
    {
        var factory = new IrFactory();
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            IrSemanticTerms.Guard(missing == "factory" ? null! : factory,
                missing == "condition" ? null! : factory.Boolean(true),
                missing == "consequence" ? null! : factory.Boolean(false))));

        Assert.That(error!.ParamName, Is.EqualTo(missing));
    }

    [Test]
    public void VariableCollectionRequiresAnExpression()
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            IrTermAnalysis.CollectVariables(null!)));

        Assert.That(error!.ParamName, Is.EqualTo("root"));
    }

    [TestCase(0, true)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(1, false)]
    [TestCase(7, true)]
    [TestCase(7, false)]
    public void BooleanCombinationsPreserveEmptySingletonAndMultipleConditions(
        int count, bool firstValue)
    {
        var factory = new IrFactory();
        var terms = new List<IrTerm>();
        var values = new Dictionary<IrVarId, IrValue>();
        var expectedConjunction = true;
        var expectedDisjunction = false;
        for (var index = 0; index < count; index++)
        {
            var variable = factory.CreateVariable("condition" + index, factory.BooleanType);
            var value = index == 0 ? firstValue : index % 2 == 0;
            terms.Add(factory.Variable(variable));
            values.Add(variable, factory.CreateBooleanValue(value));
            expectedConjunction &= value;
            expectedDisjunction |= value;
        }
        var interpreter = new IrInterpreter(factory);
        var conjunction = interpreter.Evaluate(IrSemanticTerms.Conjoin(factory, terms), values);
        var disjunction = interpreter.Evaluate(IrSemanticTerms.Disjoin(factory, terms), values);

        Assert.That(conjunction.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(disjunction.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(conjunction.Value!.Boolean, Is.EqualTo(expectedConjunction));
        Assert.That(disjunction.Value!.Boolean, Is.EqualTo(expectedDisjunction));
    }
}
