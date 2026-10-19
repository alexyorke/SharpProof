using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Frontend;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzDomainBehaviorTests
{
    [TestCase(IrExecutionSemantics.Legacy, false)]
    [TestCase(IrExecutionSemantics.Total, true)]
    public void DefinednessRespectsFactoryDivisionSemantics(
        IrExecutionSemantics semantics, bool expectedDefined)
    {
        var factory = new IrFactory(semantics);
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var formula = factory.Binary(IrBinaryOperator.Equal,
            factory.Binary(IrBinaryOperator.Divide, factory.Integer(1),
                factory.Variable(divisor)), factory.Integer(1));
        var atZero = new IrInterpreter(factory).Evaluate(formula,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(divisor, factory.CreateIntegerValue(factory.IntegerType, 0)));

        Assert.That(atZero.Status, Is.EqualTo(expectedDefined
            ? IrEvaluationStatus.Value : IrEvaluationStatus.Exception));
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(
            factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(expectedDefined));
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(
            factory, formula, TestContext.CurrentContext.CancellationToken, out var prepared),
            Is.EqualTo(expectedDefined));
        Assert.That(prepared is not null, Is.EqualTo(expectedDefined));
    }

    [Test]
    public void SemanticEdgeOracleRejectsIncompletePublicCaseDescriptions()
    {
        var complete = new FrontendSemanticEdgeCase("long", "long value", "value",
            [7L], FrontendSubsetDecision.Exact, FrontendAbstention.None);
        var incompleteCases = ImmutableArray.Create(
            complete with { ReturnType = " " },
            complete with { Parameters = null! },
            complete with { Expression = " " },
            complete with { Arguments = null! });

        foreach (var incomplete in incompleteCases)
        {
            var exception = Assert.Throws<ArgumentException>((Action)(() =>
                FrontendDifferentialOracle.CompareSemanticEdges([incomplete], cancellationToken: TestContext.CurrentContext.CancellationToken)));
            Assert.That(exception!.ParamName, Is.EqualTo("cases"));
            Assert.That(exception.Message, Does.Contain("incomplete"));
        }

        Assert.That(FrontendDifferentialOracle.CompareSemanticEdges([], cancellationToken: TestContext.CurrentContext.CancellationToken), Is.Empty);
    }
}
