using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SmtSyntacticClassifierTests
{
    [Test]
    public void SyntacticFactSetCopy_PreservesDepthAndSharesWorkBudget()
    {
        var factSetType = typeof(SmtAnalysisService).Assembly
            .GetType("SharpProof.Symbolic.Smt.SmtSyntacticClassifier+SyntacticFactSet", true)!;
        var defaultConstructor = factSetType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null)!;
        var copyConstructor = factSetType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { factSetType },
            null)!;
        var depthField = factSetType.GetField(
            "_booleanFactInferenceDepth",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var workBudgetField = factSetType.GetField(
            "_workBudget",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var source = defaultConstructor.Invoke(Array.Empty<object>());
        depthField.SetValue(source, 7);

        var copy = copyConstructor.Invoke(new[] { source });

        Assert.That(depthField.GetValue(copy), Is.EqualTo(7));
        Assert.That(workBudgetField.GetValue(copy), Is.SameAs(workBudgetField.GetValue(source)));
    }

    [Test]
    public void AffineClassifier_NormalizesLongMinValueCoefficientWithoutLosingContradiction()
    {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var scaled = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Multiply,
            new SmtIntegerConstant(long.MinValue),
            value);
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThan,
                value,
                new SmtIntegerConstant(0)),
            new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                scaled,
                new SmtIntegerConstant(0)));
        var query = new PurityProofQuery(
            pathConditions,
            new PurityHazard(PurityHazardKind.BranchReachability, new SmtBooleanConstant(true)));

        Assert.That(SmtSyntacticClassifier.TryClassify(query, pathConditions, out var result), Is.True);
        Assert.That(result.PathCheck.WasAttempted, Is.True);
        Assert.That(result.PathCheck.Feasibility, Is.EqualTo(Feasibility.Unsatisfiable));
        Assert.That(result.ImpurityCheck.WasAttempted, Is.False);
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void AffineClassifier_NegativeCoefficientPreservesAdjustedConstantSign()
    {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var negated = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, value);
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtIntegerConstant(0)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, negated, new SmtIntegerConstant(5)));
        var query = new PurityProofQuery(
            pathConditions,
            new PurityHazard(PurityHazardKind.BranchReachability, new SmtBooleanConstant(true)));

        Assert.That(SmtSyntacticClassifier.TryClassify(query, pathConditions, out _), Is.False);
    }

    [Test]
    public void AffineClassifier_NegativeCoefficientStillFindsRealContradiction()
    {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var negated = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, value);
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtIntegerConstant(0)),
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, negated, new SmtIntegerConstant(5)));
        var query = new PurityProofQuery(
            pathConditions,
            new PurityHazard(PurityHazardKind.BranchReachability, new SmtBooleanConstant(true)));

        Assert.That(SmtSyntacticClassifier.TryClassify(query, pathConditions, out var result), Is.True);
        Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
    }

    [Test]
    public void OpaqueIntegerOperation_FallsThroughToSolver()
    {
        var opaque = new SmtOpaqueIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Multiply,
            new SmtVariable("left", SmtValueKind.Int),
            new SmtVariable("right", SmtValueKind.Int));
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                opaque,
                new SmtIntegerConstant(0)));
        var query = new PurityProofQuery(
            pathConditions,
            new PurityHazard(PurityHazardKind.BranchReachability, new SmtBooleanConstant(true)));

        Assert.That(SmtSyntacticClassifier.TryClassify(query, pathConditions, out var result), Is.False);
        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo("smt_syntactic_opaque_integer_operation"));
    }

    [Test]
    public void FormulaStructuralKey_IsIndependentOfCultureAndAllocationOrder()
    {
        static SmtFormula CreateFormula()
        {
            return new SmtConditionalFormula(
                new SmtVariable("condition", SmtValueKind.Bool),
                new SmtStringConcatTerm(
                    new SmtStringConstant("left"),
                    new SmtVariable("value", SmtValueKind.String)),
                new SmtStringConstant("right"),
                SmtValueKind.String);
        }

        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var first = SmtFormulaStructuralKey.Create(CreateFormula());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = SmtFormulaStructuralKey.Create(CreateFormula());

            Assert.That(second, Is.EqualTo(first));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
