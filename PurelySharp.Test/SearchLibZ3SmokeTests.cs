using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SearchLibZ3SmokeTests
    {
        [Test]
        public void SmtSolver_TrueAndFalseConjunction_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBooleanConstant(true),
                    new SmtBooleanConstant(false),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NonZeroGuardDoesNotImplyZero_IsSatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xNotZero = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(0));
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

            var result = solver.Implies(
                new[] { xNotZero },
                xIsZero,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_ZeroGuardImpliesZero_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

            var result = solver.Implies(
                new[] { xIsZero },
                xIsZero,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_AffineEqualityAndConflictingInequality_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(1));
            var affineEquality = new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusOne, new SmtIntegerConstant(0));
            var xIsNonNegative = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0));

            var result = solver.IsSatisfiable(
                new SmtFormula[] { affineEquality, xIsNonNegative },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_UnsupportedRegexWithoutConcreteInput_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, "("),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_InvalidRegexCategoryWithoutConcreteInput_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\p{NotARealCategory}\z"),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_FinalNewlineRegexAnchorImpliesBoundedLength()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var boundedLength = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(3));

            var result = solver.Implies(
                new[]
                {
                    new SmtRegexMatchFormula(text, @"\AAB\Z"),
                },
                boundedLength,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_EscapedRegexClassLiteralContradictsPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[\.\]]\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_LeadingBracketRegexClassLiteralContradictsPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[]]\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegatedApproximateRegexWithLength_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtUnaryFormula(
                        SmtUnaryOperator.Not,
                        new SmtRegexMatchFormula(text, @"\A\bA\z")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_ApproximateRegexSatisfiableResult_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\bA\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_DigitRegexContradictsNonDigitPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\d\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NonDigitRegexContradictsSingleDigitPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\D\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("5")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegatedDigitClassContradictsSingleDigitPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[^\d]\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("5")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_WhitespaceRegexContradictsNonWhitespacePrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\s\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NonWhitespaceRegexContradictsNewlinePrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\S\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("\n")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_WordRegexContradictsPunctuationPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\w\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("!")),
                },
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NonWordRegexContradictsUnderscorePrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\W\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("_")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_UnicodeCategoryRegexContradictsLetterPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\p{P}\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegatedUnicodeCategoryRegexContradictsPunctuationPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\P{P}\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("!")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegatedUnicodeCategoryClassContradictsPunctuationPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[^\p{P}]\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("!")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_LargeUnicodeCategoryConclusionDoesNotBecomeProof()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var lengthIsOne = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(1));
            var textIsUppercaseLetter = new SmtRegexMatchFormula(text, @"\A\p{Lu}\z");

            var result = solver.Implies(
                new[] { lengthIsOne },
                textIsUppercaseLetter,
                TimeSpan.FromMilliseconds(250));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_ApproximateRegexPathStillProvesLengthImplication()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var lengthIsOne = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(1));

            var result = solver.Implies(
                new[]
                {
                    new SmtRegexMatchFormula(text, @"\A\bA\z"),
                },
                lengthIsOne,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_ApproximateRegexConclusionDoesNotBecomeProof()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var lengthIsOne = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(1));
            var textIsBoundaryA = new SmtRegexMatchFormula(text, @"\A\bA\z");

            var result = solver.Implies(
                new[] { lengthIsOne },
                textIsBoundaryA,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_NonPositiveTimeout_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(1)),
                },
                TimeSpan.Zero);

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_MismatchedEqualitySorts_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var intValue = new SmtVariable("mixed", SmtValueKind.Int);
            var stringValue = new SmtVariable("mixed", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, intValue, stringValue),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_ConditionalIntegerTermHonorsSelectedBranch()
        {
            using var solver = new SmtSolver();
            var useFirstBranch = new SmtVariable("useFirstBranch", SmtValueKind.Bool);
            var selectedValue = new SmtConditionalFormula(
                useFirstBranch,
                new SmtIntegerConstant(1),
                new SmtIntegerConstant(2),
                SmtValueKind.Int);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    useFirstBranch,
                    new SmtBinaryFormula(
                        SmtBinaryOperator.NotEqual,
                        selectedValue,
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_AffineGuardImpliesExactValue_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xMinusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, x, new SmtIntegerConstant(1));
            var guard = new SmtBinaryFormula(SmtBinaryOperator.Equal, xMinusOne, new SmtIntegerConstant(0));
            var conclusion = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(1));

            var result = solver.Implies(
                new[] { guard },
                conclusion,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_MultiplicationByConstantContradictsRange()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var twiceX = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, x, new SmtIntegerConstant(2));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(5)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, twiceX, new SmtIntegerConstant(10)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_StringPrefixSuffixAndLengthFactsCombine()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                    new SmtStringEndsWithFormula(text, new SmtStringConstant("CD")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(3)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_StringContainsAndConcatFactsCombine()
        {
            using var solver = new SmtSolver();
            var left = new SmtVariable("left", SmtValueKind.String);
            var right = new SmtVariable("right", SmtValueKind.String);
            var combined = new SmtStringConcatTerm(left, right);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, left, new SmtStringConstant("A")),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, right, new SmtStringConstant("B")),
                    new SmtUnaryFormula(
                        SmtUnaryOperator.Not,
                        new SmtStringContainsFormula(combined, new SmtStringConstant("AB"))),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_StringContainsLongerThanKnownLength_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(2)),
                    new SmtStringContainsFormula(text, new SmtStringConstant("ABC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_StringContainsWithExactLengthInfersValue()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(2)),
                    new SmtStringContainsFormula(text, new SmtStringConstant("AB")),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegativeStringLengthBound_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(
                        SmtBinaryOperator.LessThan,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(0)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_DivideByConcreteZero_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var division = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Divide,
                new SmtIntegerConstant(10),
                new SmtIntegerConstant(0));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(0)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_DivideByZeroFromEquality_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var divisor = new SmtVariable("divisor", SmtValueKind.Int);
            var division = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Divide,
                new SmtIntegerConstant(10),
                divisor);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(0)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_DivisionWithNonZeroGuard_RemainsUsable()
        {
            using var solver = new SmtSolver();
            var divisor = new SmtVariable("divisor", SmtValueKind.Int);
            var division = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Divide,
                new SmtIntegerConstant(10),
                divisor);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(2)),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, division, new SmtIntegerConstant(5)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_RemainderWithRelationalNonZeroGuard_RemainsUsable()
        {
            using var solver = new SmtSolver();
            var divisor = new SmtVariable("divisor", SmtValueKind.Int);
            var remainder = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Remainder,
                new SmtIntegerConstant(5),
                divisor);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, divisor, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(2)),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, remainder, new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_ReferenceNullAndNonNullConjunction_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var reference = new SmtVariable("reference", SmtValueKind.Reference);
            var isNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, reference, new SmtNullConstant());
            var isNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, reference, new SmtNullConstant());

            var result = solver.IsSatisfiable(
                new SmtFormula[] { isNull, isNotNull },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void PurityProof_NonNullGuard_MakesNullDereferenceProvablyPure()
        {
            using var search = new PurityProofSearch();
            var s = new SmtVariable("s", SmtValueKind.Reference);
            var sIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, s, new SmtNullConstant());
            var sIsNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, s, new SmtNullConstant());

            var result = search.ClassifyNullDereference(
                new[] { sIsNotNull },
                sIsNull,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("null_dereference_unreachable"));
        }
    }
}
