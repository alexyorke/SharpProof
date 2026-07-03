using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;
using System.Text.RegularExpressions;

namespace PurelySharp.Test
{
    [TestFixture]
    [NonParallelizable]
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
        public void SmtSolver_CheckPathAndImpurity_PreservesPathEqualitiesForCombinedQuery()
        {
            using var solver = new SmtSolver();
            var length = new SmtVariable("length", SmtValueKind.Int);
            var arrayLength = new SmtVariable("values.Length", SmtValueKind.Int);
            var conditions = new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, length, new SmtIntegerConstant(4)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, arrayLength, length),
            };
            var inRange = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, length, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, length, arrayLength));

            var result = solver.CheckPathAndImpurity(
                conditions,
                inRange,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result.PathFeasibility, Is.EqualTo(Feasibility.Satisfiable));
            Assert.That(result.ImpurityFeasibility, Is.EqualTo(Feasibility.Unsatisfiable));
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
        public void SmtSolver_UnsupportedRegexOptionsWithoutConcreteInput_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.IgnoreCase),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_UnsupportedRegexOptionsConcreteMismatchUsesDotNetOptions()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.IgnoreCase),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("CD")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_MultilineOptionStrictAnchorsRemainExact()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var textIsAb = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                text,
                new SmtStringConstant("AB"));

            var result = solver.Implies(
                new[]
                {
                    new SmtRegexMatchFormula(text, @"\AAB\z", RegexOptions.Multiline),
                },
                textIsAb,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_MultilineCaretAnchorWithoutConcreteInput_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, "^AB", RegexOptions.Multiline),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_LeadingContiguousAnchorRegexAcceptsInitialMatch()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\GAB"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_LeadingContiguousAnchorRegexContradictsLaterMatch()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\GAB"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("XAB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_InternalContiguousAnchorRegexWithoutConcreteInput_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\AA\GB\z"),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_CultureInvariantIgnoreCaseRegexImpliesLiteralLength()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var lengthIsTwo = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(2));

            var result = solver.Implies(
                new[]
                {
                    new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                },
                lengthIsTwo,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_CultureInvariantIgnoreCaseRegexAcceptsCaseVariantLiteral()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_CultureInvariantIgnoreCaseCharClassAcceptsUppercaseVariant()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[a-c]\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("B")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
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
        public void SmtSolver_InlineOptionBeforeStrictAnchorsImpliesLiteralLength()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var lengthIsTwo = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(2));

            var result = solver.Implies(
                new[]
                {
                    new SmtRegexMatchFormula(text, @"(?i)\Aab\z", RegexOptions.CultureInvariant),
                },
                lengthIsTwo,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_IgnorePatternWhitespaceBeforeStrictStartAnchorSkipsTrivia()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var pattern = "(?x) # leading trivia\n \\A A B \\z";

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, pattern),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_InlineSinglelineBeforeCaretAnchorAllowsNewlineDot()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"(?s)^.\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_InlineOptionBeforeDollarAnchorImpliesBoundedFinalNewlineLength()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var boundedLength = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(2));

            var result = solver.Implies(
                new[]
                {
                    new SmtRegexMatchFormula(text, "(?x)^ A $"),
                },
                boundedLength,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_IgnorePatternWhitespaceGroupSkipsWhitespaceAndComments()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var pattern = "\\A(?x:A B # ignored comment\n C)\\z";

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, pattern),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("ABC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_IgnorePatternWhitespaceGroupKeepsEscapedSpaceLiteral()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, "\\A(?x:A\\ B)\\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("A B")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_DefaultDotRejectsNewline()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A.\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_InlineSinglelineDotAllowsNewline()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?s:.)\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_ScopedSinglelineDisableDotRejectsNewline()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?s:A(?-s:.)C)\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A\nC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_InlineIgnoreCaseOptionGroupAffectsFollowingLiterals()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?i)ab\z", RegexOptions.CultureInvariant),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_InlineIgnoreCaseDisableMakesFollowingLiteralCaseSensitive()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?i)ab(?-i)c\z", RegexOptions.CultureInvariant),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_InlineIgnorePatternWhitespaceOptionSkipsRemainderTrivia()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);
            var pattern = "\\A(?x)A B # ignored comment\n C\\z";

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, pattern),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("ABC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_InlineSinglelineDisableMakesFollowingDotRejectsNewline()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?s).(?-s).\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n\n")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_InlineRegexCommentBeforeQuantifierPreservesPreviousAtom()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\AA(?# repeat previous atom)*\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AA")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_LeadingInlineRegexCommentBeforeStartAnchorKeepsAnchorStrict()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"(?# leading comment)\AAB\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("XAB")),
                },
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
        public void SmtSolver_CharacterClassSubtractionRejectsExcludedLiteral()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[a-z-[aeiou]]\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("a")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_CharacterClassSubtractionAllowsRemainingLiteral()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[a-z-[aeiou]]\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("b")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_ControlCharacterEscapeAllowsExpectedCharacter()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\cA\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("\u0001")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_ControlCharacterClassEscapeContradictsDifferentCharacter()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[\cA]\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("\u0002")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_OctalRegexEscapeImpliesSpaceLiteral()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\040\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant(" ")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_OctalRegexEscapeConsumesAtMostTwoFollowingDigits()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A\0408\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant(" 8")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_OctalRegexClassEscapeContradictsDifferentCharacter()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[\040]\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_PositiveLookaheadRegexContradictsImpossibleSuffix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?=AB)A.\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_PositiveLookaheadRegexAcceptsMatchingSuffix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?=AB)A.\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_NegativeLookaheadRegexRejectsExcludedSuffix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?!AB)A.\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegativeLookaheadRegexAcceptsDifferentSuffix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?!AB)A.\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_LookaheadWithoutConsumingSuffix_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\AA(?=B)"),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_PositiveLookbehindRegexContradictsImpossiblePrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<=AB)C\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AAC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_PositiveLookbehindRegexAcceptsMatchingPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<=AB)C\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_NegativeLookbehindRegexRejectsExcludedPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<!AB)C\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegativeLookbehindRegexAcceptsDifferentPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<!AB)C\z"),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AAC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_LookbehindWithoutParsedPrefix_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?<=A)B\z"),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_AtomicGroupRegexContradictsWrongPrefix()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?>A*)A\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("B")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_AtomicGroupApproximateSatisfiableResult_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\A(?>A*)A\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
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
                        new SmtRegexMatchFormula(text, @"\A(?>A*)A\z")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_WordBoundaryRegexSatisfiableResult_IsUnknown()
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
        public void SmtSolver_WordBoundaryBetweenWordsIsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\AA\bB\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NonWordBoundaryBetweenWordsIsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\AA\BB\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_NonWordBoundaryBetweenWordAndPunctuationIsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, @"\AA\B!\z"),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("A!")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
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
        public void SmtSolver_WordBoundaryRegexPathProvesLengthImplication()
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
        public void SmtSolver_WordBoundaryRegexConclusionRemainsUnknown()
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
        public void SmtSolver_NegativeDividendDivision_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var dividend = new SmtVariable("dividend", SmtValueKind.Int);
            var division = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Divide,
                dividend,
                new SmtIntegerConstant(2));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, dividend, new SmtIntegerConstant(-2)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, dividend, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(0)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_NegativeDividendRemainder_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var dividend = new SmtVariable("dividend", SmtValueKind.Int);
            var remainder = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Remainder,
                dividend,
                new SmtIntegerConstant(2));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, dividend, new SmtIntegerConstant(-2)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, dividend, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, remainder, new SmtIntegerConstant(-1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_NegativeDivisorDivision_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var divisor = new SmtVariable("divisor", SmtValueKind.Int);
            var division = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Divide,
                new SmtIntegerConstant(3),
                divisor);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, divisor, new SmtIntegerConstant(-3)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, divisor, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(-1)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_NonNegativeRemainderWithPositiveDivisor_ProvesRange()
        {
            using var solver = new SmtSolver();
            var dividend = new SmtVariable("dividend", SmtValueKind.Int);
            var divisor = new SmtVariable("divisor", SmtValueKind.Int);
            var remainder = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Remainder,
                dividend,
                divisor);

            var outOfRange = new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, remainder, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, remainder, divisor));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, dividend, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, divisor, new SmtIntegerConstant(0)),
                    outOfRange,
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_SelectedConditionalSkipsUnsafeUnselectedIntegerBranch()
        {
            using var solver = new SmtSolver();
            var useSafeBranch = new SmtVariable("useSafeBranch", SmtValueKind.Bool);
            var unsafeBranch = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Divide,
                new SmtIntegerConstant(1),
                new SmtIntegerConstant(0));
            var selectedValue = new SmtConditionalFormula(
                useSafeBranch,
                new SmtIntegerConstant(7),
                unsafeBranch,
                SmtValueKind.Int);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    useSafeBranch,
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, selectedValue, new SmtIntegerConstant(7)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NegatedComparisonGuardKeepsDivisionUsable()
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
                    new SmtUnaryFormula(
                        SmtUnaryOperator.Not,
                        new SmtBinaryFormula(
                            SmtBinaryOperator.LessThanOrEqual,
                            divisor,
                            new SmtIntegerConstant(0))),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, division, new SmtIntegerConstant(999)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_AffineRangeGuardKeepsDerivedDivisorUsable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var divisor = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Subtract,
                x,
                new SmtIntegerConstant(1));
            var division = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Divide,
                new SmtIntegerConstant(10),
                divisor);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(1)),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(10)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_AffineEqualityPropagatesSolvedValue()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xPlusTwo = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Add,
                x,
                new SmtIntegerConstant(2));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusTwo, new SmtIntegerConstant(5)),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(3)),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_ConflictingStringPrefixesAreUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("AC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_ExactLengthPrefixSuffixOverlapInfersString()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                    new SmtStringEndsWithFormula(text, new SmtStringConstant("BC")),
                    new SmtBinaryFormula(
                        SmtBinaryOperator.Equal,
                        new SmtStringLengthTerm(text),
                        new SmtIntegerConstant(3)),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("ABC")),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_SelectedConditionalStringConcatSimplifiesPredicate()
        {
            using var solver = new SmtSolver();
            var usePrefix = new SmtVariable("usePrefix", SmtValueKind.Bool);
            var selected = new SmtConditionalFormula(
                usePrefix,
                new SmtStringConstant("AB"),
                new SmtStringConstant("CD"),
                SmtValueKind.String);
            var combined = new SmtStringConcatTerm(selected, new SmtStringConstant("X"));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    usePrefix,
                    new SmtUnaryFormula(
                        SmtUnaryOperator.Not,
                        new SmtStringStartsWithFormula(combined, new SmtStringConstant("AB"))),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_BooleanAliasChainContradiction_IsPreprocessedWithoutZ3()
        {
            using var solver = new SmtSolver();
            var first = new SmtVariable("first", SmtValueKind.Bool);
            var second = new SmtVariable("second", SmtValueKind.Bool);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, first, second),
                    first,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, second),
                },
                TimeSpan.Zero);

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_IntegerAliasIntervalContradiction_IsPreprocessedWithoutZ3()
        {
            using var solver = new SmtSolver();
            var alias = new SmtVariable("alias", SmtValueKind.Int);
            var source = new SmtVariable("source", SmtValueKind.Int);
            var aliasPlusOne = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Add,
                alias,
                new SmtIntegerConstant(1));

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, alias, source),
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, source, new SmtIntegerConstant(5)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, aliasPlusOne, new SmtIntegerConstant(5)),
                },
                TimeSpan.Zero);

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_StringAliasPredicateContradiction_IsPreprocessedWithoutZ3()
        {
            using var solver = new SmtSolver();
            var copy = new SmtVariable("copy", SmtValueKind.String);
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, copy, text),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
                    new SmtUnaryFormula(
                        SmtUnaryOperator.Not,
                        new SmtStringStartsWithFormula(copy, new SmtStringConstant("A"))),
                },
                TimeSpan.Zero);

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_ReferenceAliasNullContradiction_IsPreprocessedWithoutZ3()
        {
            using var solver = new SmtSolver();
            var source = new SmtVariable("source", SmtValueKind.Reference);
            var target = new SmtVariable("target", SmtValueKind.Reference);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, target, source),
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, source, new SmtNullConstant()),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, target, new SmtNullConstant()),
                },
                TimeSpan.Zero);

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_DistinctNonNullReferencesRemainUnknownWithoutZ3()
        {
            using var solver = new SmtSolver();
            var left = new SmtVariable("left", SmtValueKind.Reference);
            var right = new SmtVariable("right", SmtValueKind.Reference);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, new SmtNullConstant()),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant()),
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right),
                },
                TimeSpan.Zero);

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_IdenticalConditionalBranchesSimplifyWithoutZ3()
        {
            using var solver = new SmtSolver();
            var flag = new SmtVariable("flag", SmtValueKind.Bool);
            var value = new SmtVariable("value", SmtValueKind.Int);
            var valuePlusOne = new SmtIntegerBinaryTerm(
                SmtIntegerBinaryOperator.Add,
                value,
                new SmtIntegerConstant(1));
            var selected = new SmtConditionalFormula(
                flag,
                valuePlusOne,
                valuePlusOne,
                SmtValueKind.Int);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, selected, valuePlusOne),
                },
                TimeSpan.Zero);

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_ConcatLengthNegativeComparison_IsPreprocessedWithoutZ3()
        {
            using var solver = new SmtSolver();
            var left = new SmtVariable("left", SmtValueKind.String);
            var right = new SmtVariable("right", SmtValueKind.String);
            var combined = new SmtStringConcatTerm(left, right);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(
                        SmtBinaryOperator.LessThan,
                        new SmtStringLengthTerm(combined),
                        new SmtIntegerConstant(0)),
                },
                TimeSpan.Zero);

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
