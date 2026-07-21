using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SmtConcreteFactIndexTests {
    [TestCase(0, (int)SmtBinaryOperator.Equal)]
    [TestCase(1, (int)SmtBinaryOperator.NotEqual)]
    [TestCase(2, (int)SmtBinaryOperator.Equal)]
    public void ComparisonExtraction_PreservesNestedNegationParity(
        int negationCount,
        int expectedOperatorValue) {
        var expectedOperator = (SmtBinaryOperator)expectedOperatorValue;
        SmtFormula formula = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtVariable("value", SmtValueKind.Int),
            new SmtIntegerConstant(0));
        for (var index = 0; index < negationCount; index++)
            formula = new SmtUnaryFormula(SmtUnaryOperator.Not, formula);

        Assert.That(
            SmtComparisonOperatorFacts.TryExtract(formula, out var comparison, out var actualNegationCount),
            Is.True);
        Assert.Multiple(() => {
            Assert.That(comparison.Operator, Is.EqualTo(SmtBinaryOperator.Equal));
            Assert.That(actualNegationCount, Is.EqualTo(negationCount));
            Assert.That(
                SmtComparisonOperatorFacts.ApplyNegations(comparison.Operator, actualNegationCount),
                Is.EqualTo(expectedOperator));
        });
    }

    [TestCase("integer")]
    [TestCase("string")]
    [TestCase("reference")]
    public void ComparisonExtraction_DomainFactsRetainContradictionDetection(string domain) {
        (SmtFormula left, SmtFormula right) = domain switch {
            "integer" => ((SmtFormula)new SmtVariable("value", SmtValueKind.Int),
                (SmtFormula)new SmtIntegerConstant(0)),
            "string" => ((SmtFormula)new SmtVariable("value", SmtValueKind.String),
                (SmtFormula)new SmtStringConstant("known")),
            "reference" => ((SmtFormula)new SmtVariable("value", SmtValueKind.Reference),
                (SmtFormula)new SmtNullConstant()),
            _ => throw new ArgumentOutOfRangeException(nameof(domain))
        };
        var equality = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            equality,
            new SmtUnaryFormula(SmtUnaryOperator.Not, equality));
        AssertPreparationStatus(pathConditions, SmtConcreteFactPreparationStatus.Unsatisfiable);
    }

    [Test]
    public void ConcreteFactSetCopy_PreservesDepthAndSharesWorkBudget() {
        var factSetType = typeof(SmtFormula).Assembly
            .GetType("SharpProof.ProofCore.Smt.SmtConcreteFactIndex", true)!;
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
    public void AffineConcreteFacts_NormalizeLongMinValueCoefficientWithoutLosingContradiction() {
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
        AssertPreparationStatus(pathConditions, SmtConcreteFactPreparationStatus.Unsatisfiable);
    }

    [Test]
    public void AffineConcreteFacts_NegativeCoefficientPreservesAdjustedConstantSign() {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var negated = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, value);
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtIntegerConstant(0)),
            new SmtBinaryFormula(SmtBinaryOperator.LessThan, negated, new SmtIntegerConstant(5)));
        AssertPreparationStatus(pathConditions, SmtConcreteFactPreparationStatus.Ready);
    }

    [Test]
    public void AffineConcreteFacts_NegativeCoefficientStillFindsRealContradiction() {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var negated = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, value);
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            new SmtBinaryFormula(SmtBinaryOperator.Equal, value, new SmtIntegerConstant(0)),
            new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, negated, new SmtIntegerConstant(5)));
        AssertPreparationStatus(pathConditions, SmtConcreteFactPreparationStatus.Unsatisfiable);
    }

    [Test]
    public void AffineIntegerTerm_SharedParserPreservesScaleAndOffset() {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var formula = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Subtract,
            new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Multiply,
                new SmtIntegerConstant(2),
                value),
            new SmtIntegerConstant(3));

        static bool ResolveConstant(SmtFormula candidate, out long constant) {
            if (candidate is SmtIntegerConstant integer) {
                constant = integer.Value;
                return true;
            }

            constant = default;
            return false;
        }

        Assert.That(
            SmtAffineIntegerTerm.TryCreate(
                formula,
                8,
                static candidate => candidate,
                ResolveConstant,
                false,
                static candidate => candidate is SmtVariable { Kind: SmtValueKind.Int },
                out var affine),
            Is.True);
        Assert.Multiple(() => {
            Assert.That(affine.BaseTerm, Is.EqualTo(value));
            Assert.That(affine.Scale, Is.EqualTo(2));
            Assert.That(affine.Offset, Is.EqualTo(-3));
        });
    }

    [Test]
    public void IntegerInterval_IntersectionPreservesBoundsAndExclusions() {
        var interval = SmtIntegerInterval.Unbounded
            .Apply(SmtBinaryOperator.GreaterThanOrEqual, -2)
            .Apply(SmtBinaryOperator.LessThanOrEqual, 2)
            .Apply(SmtBinaryOperator.NotEqual, 0);

        Assert.Multiple(() => {
            Assert.That(interval.LowerBound, Is.EqualTo(-2));
            Assert.That(interval.UpperBound, Is.EqualTo(2));
            Assert.That(interval.Excludes(0), Is.True);
            Assert.That(interval.Excludes(1), Is.False);
            Assert.That(interval.IsContradictory, Is.False);
        });

        Assert.That(
            interval.Intersect(
                SmtIntegerInterval.Unbounded.Apply(SmtBinaryOperator.Equal, 0)).IsContradictory,
            Is.True);
    }

    [TestCase((int)SmtBinaryOperator.GreaterThan, long.MaxValue)]
    [TestCase((int)SmtBinaryOperator.LessThan, long.MinValue)]
    public void IntegerInterval_ImpossibleStrictBoundaryIsContradictory(int operatorValue, long constant) {
        var interval = SmtIntegerInterval.Unbounded.Apply((SmtBinaryOperator)operatorValue, constant);

        Assert.That(interval.IsContradictory, Is.True);
    }

    [Test]
    public void OpaqueIntegerOperation_RemainsAvailableForSolverEncoding() {
        var opaque = new SmtOpaqueIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Multiply,
            new SmtVariable("left", SmtValueKind.Int),
            new SmtVariable("right", SmtValueKind.Int));
        var pathConditions = ImmutableArray.Create<SmtFormula>(
            new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                opaque,
                new SmtIntegerConstant(0)));
        AssertPreparationStatus(pathConditions, SmtConcreteFactPreparationStatus.Ready);
    }

    [Test]
    public void FormulaStructuralKey_IsIndependentOfCultureAndAllocationOrder() {
        static SmtFormula CreateFormula() {
            return new SmtConditionalFormula(
                new SmtVariable("condition", SmtValueKind.Bool),
                new SmtStringConcatTerm(
                    new SmtStringConstant("left"),
                    new SmtVariable("value", SmtValueKind.String)),
                new SmtStringConstant("right"),
                SmtValueKind.String);
        }

        var previousCulture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var first = SmtFormulaStructuralKey.Create(CreateFormula());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = SmtFormulaStructuralKey.Create(CreateFormula());

            Assert.That(second, Is.EqualTo(first));
        }
        finally {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static void AssertPreparationStatus(
        ImmutableArray<SmtFormula> conditions,
        SmtConcreteFactPreparationStatus expected) {
        var status = new SmtConcreteFactPreprocessor().Prepare(conditions.ToArray(), out _);
        Assert.That(status, Is.EqualTo(expected));
    }
}
