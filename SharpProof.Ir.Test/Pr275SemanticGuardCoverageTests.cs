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
}
