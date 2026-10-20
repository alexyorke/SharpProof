using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class Pr275FiniteDomainInputBoundaryTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void CandidateEnumerationRejectsMissingOwnedInputs(bool missingFactory)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var term = factory.Boolean(true);
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            _ = IrStructuralShrinker.GetCandidates(missingFactory ? null! : factory, missingFactory ? term : null!);
        }));
        Assert.That(error!.ParamName, Is.EqualTo(missingFactory ? "factory" : "term"));
        Assert.That(IrStructuralShrinker.GetCandidates(factory, term), Is.Empty);
        Assert.That(IrStructuralShrinker.StructuralSize(term), Is.EqualTo(1));
    }

    [Test]
    public void StructuralSizeRejectsMissingTerm()
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() => { _ = IrStructuralShrinker.StructuralSize(null!); }));
        Assert.That(error!.ParamName, Is.EqualTo("term"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FormulaValidationRejectsMissingOwnedInputs(bool missingFactory)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var formula = factory.Boolean(true);
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            _ = FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(
                missingFactory ? null! : factory, missingFactory ? formula : null!, cancellationToken: TestContext.CurrentContext.CancellationToken);
        }));
        Assert.That(error!.ParamName, Is.EqualTo(missingFactory ? "factory" : "formula"));
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
    }

    [Test]
    public void OwnedIntegerTermIsRejectedAsANonBooleanFormula()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var error = Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, factory.Integer(1), cancellationToken: TestContext.CurrentContext.CancellationToken);
        }));
        Assert.That(error!.ParamName, Is.EqualTo("formula"));
        Assert.That(error.Message, Does.Contain("must be Boolean"));
    }

    [Test]
    public async Task OwnedStringEqualityAbstainsOutsideTheFiniteScalarDomain()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.CreateVariable("text", factory.StringType);
        var formula = factory.Binary(IrBinaryOperator.Equal, factory.Variable(text), factory.String("sample"));
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.False);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            TestContext.CurrentContext.CancellationToken, out var prepared), Is.False);
        Assert.That(prepared, Is.Null);
        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
        Assert.That(result.Actual, Is.Null);
        Assert.That(result.FiniteDomainAssumptions, Is.Zero);
        Assert.That(result.Detail, Does.Contain("outside the finite Boolean/integer domain"));
    }

    [Test]
    public void TotalOwnedDivisionRemainsDefinedAndProducesActualPreparation()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var division = factory.Binary(IrBinaryOperator.Divide, factory.Integer(1), factory.Variable(value));
        var formula = factory.Binary(IrBinaryOperator.GreaterThan, division, factory.Integer(0));
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            TestContext.CurrentContext.CancellationToken, out var prepared), Is.True);
        Assert.That(prepared, Is.Not.Null);
        Assert.That(prepared!.Formula, Is.SameAs(formula));
        Assert.That(prepared.Variables, Is.EqualTo(ImmutableArray.Create(value)));
        Assert.That(prepared.AssignmentCount, Is.EqualTo(FiniteDomainSmtDifferentialOracle.IntegerDomain.Length));
        Assert.That(prepared.AnyTrue, Is.True);
        var atZero = new IrInterpreter(factory).Evaluate(factory.Binary(IrBinaryOperator.Divide,
            factory.Integer(1), factory.Integer(0)));
        Assert.That(atZero.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(atZero.Value!.Integer, Is.EqualTo(-1));
    }
    [Test]
    public void LegacyOwnedDivisionDoesNotProduceAPreparedFormula()
    {
        var factory = new IrFactory(IrExecutionSemantics.Legacy);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var division = factory.Binary(IrBinaryOperator.Divide, factory.Integer(1), factory.Variable(value));
        var formula = factory.Binary(IrBinaryOperator.GreaterThan, division, factory.Integer(0));
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.False);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            TestContext.CurrentContext.CancellationToken, out var prepared), Is.False);
        Assert.That(prepared, Is.Null);
    }

    [Test]
    public async Task GuardedOwnedDivisionRetainsPreparedAndDirectNativeAgreement()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var variable = factory.Variable(value);
        var division = factory.Binary(IrBinaryOperator.Divide, factory.Integer(1), variable);
        var nonzero = factory.Binary(IrBinaryOperator.NotEqual, variable, factory.Integer(0));
        var positive = factory.Binary(IrBinaryOperator.GreaterThan, division, factory.Integer(0));
        var formula = factory.Binary(IrBinaryOperator.AndAlso, nonzero, positive);
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            TestContext.CurrentContext.CancellationToken, out var prepared), Is.True);
        Assert.That(prepared, Is.Not.Null);
        Assert.That(prepared!.Formula, Is.SameAs(formula));
        Assert.That(prepared.Variables, Is.EqualTo(ImmutableArray.Create(value)));
        Assert.That(prepared.AssignmentCount, Is.EqualTo(FiniteDomainSmtDifferentialOracle.IntegerDomain.Length));
        Assert.That(prepared.AnyTrue, Is.True);
        var direct = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
        var projected = await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(factory, prepared, cancellationToken: TestContext.CurrentContext.CancellationToken).ConfigureAwait(false);
        Assert.That(direct.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(direct.Expected, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(direct.Actual, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(direct.FiniteDomainAssumptions, Is.EqualTo(1));
        Assert.That(projected, Is.EqualTo(direct));
    }
}
