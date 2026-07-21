using System.Text.RegularExpressions;
using NUnit.Framework;
using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Test;

[TestFixture]
internal class ProofCoreZ3SmokeTests
{
    private static readonly TimeSpan SolverTimeout = TimeSpan.FromMilliseconds(50);

    [TestCase("^[a-z]+$", RegexTranslationFallback.None)]
    [TestCase("[", RegexTranslationFallback.InvalidPattern)]
    public void RegexTranslationValidator_ClassifiesInput(string pattern, RegexTranslationFallback expected)
    {
        Assert.That(Z3RegexTranslationValidator.Validate(pattern, RegexOptions.CultureInvariant), Is.EqualTo(expected));
    }

    [Test]
    public void RegexTranslationValidator_ClassifiesOversizedPatternBeforeParsing()
    {
        Assert.That(
            Z3RegexTranslationValidator.Validate(new string('a', 257), RegexOptions.None),
            Is.EqualTo(RegexTranslationFallback.PatternTooLong));
    }

    private static void AssertSatisfiability(
        Feasibility expected,
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan? timeout = null)
    {
        using var solver = new SmtSolver();
        Assert.That(
            solver.CheckSatisfiability(pathConditions, timeout ?? SolverTimeout).Feasibility,
            Is.EqualTo(expected));
    }

    private static void AssertImplication(
        Feasibility expected,
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula conclusion,
        TimeSpan? timeout = null)
    {
        using var solver = new SmtSolver();
        Assert.That(
            solver.CheckSatisfiability(
                    pathConditions.Concat(new[] { new SmtUnaryFormula(SmtUnaryOperator.Not, conclusion) }),
                    timeout ?? SolverTimeout)
                .Feasibility,
            Is.EqualTo(expected));
    }

    [Test]
    public void SmtSolver_TrueAndFalseConjunction_IsUnsatisfiable()
    {
        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBooleanConstant(true),
                new SmtBooleanConstant(false)
            });
    }

    [Test]
    public void SmtSolver_CheckSatisfiability_ExposesTypedExactAssignments()
    {
        using var solver = new SmtSolver();
        var count = new SmtVariable("count", SmtValueKind.Int);
        var enabled = new SmtVariable("enabled", SmtValueKind.Bool);
        var text = new SmtVariable("text", SmtValueKind.String);
        var receiver = new SmtVariable("receiver", SmtValueKind.Reference);

        var result = solver.CheckSatisfiability(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, count, new SmtIntegerConstant(3)),
                enabled,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("abc")),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, receiver, new SmtNullConstant())
            },
            TimeSpan.FromMilliseconds(100));

        Assert.That(result.Feasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(result.Witness.Status, Is.EqualTo(SmtWitnessStatus.Exact));
        Assert.Multiple(() =>
        {
            Assert.That(result.Witness.Assignments.Single(assignment => assignment.Name == "count").IntegerValue,
                Is.EqualTo(3));
            Assert.That(result.Witness.Assignments.Single(assignment => assignment.Name == "enabled").BooleanValue,
                Is.True);
            Assert.That(result.Witness.Assignments.Single(assignment => assignment.Name == "text").StringValue,
                Is.EqualTo("abc"));
            Assert.That(result.Witness.Assignments.Single(assignment => assignment.Name == "receiver").IsNull,
                Is.True);
        });
    }

    [Test]
    public void SmtSolver_CheckSatisfiability_ExposesRangeModel()
    {
        using var solver = new SmtSolver();
        var index = new SmtVariable("index", SmtValueKind.Int);

        var result = solver.CheckSatisfiability(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, index, new SmtIntegerConstant(2)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, index, new SmtIntegerConstant(5))
            },
            TimeSpan.FromMilliseconds(100));

        var assignment = result.Witness.Assignments.Single();
        Assert.That(result.Feasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(assignment.IntegerValue, Is.InRange(2, 4));
    }

    [Test]
    public void SmtSolver_CheckSatisfiability_MarksOpaqueReferenceModelApproximate()
    {
        using var solver = new SmtSolver();
        var receiver = new SmtVariable("receiver", SmtValueKind.Reference);

        var result = solver.CheckSatisfiability(
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, receiver, new SmtNullConstant())
            },
            TimeSpan.FromMilliseconds(100));

        Assert.That(result.Feasibility, Is.EqualTo(Feasibility.Satisfiable));
        Assert.That(result.Witness.Status, Is.EqualTo(SmtWitnessStatus.Approximate));
        Assert.That(result.Witness.Assignments.Single().IsNull, Is.False);
    }

    [Test]
    public void SmtSolver_CheckSatisfiability_PreservesApproximateRegexCandidateModel()
    {
        using var solver = new SmtSolver();
        var text = new SmtVariable("text", SmtValueKind.String);

        var result = solver.CheckSatisfiability(
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "(?>ab)c")
            },
            TimeSpan.FromMilliseconds(100));

        Assert.That(result.Feasibility, Is.EqualTo(Feasibility.Unknown));
        Assert.That(result.Witness.Status, Is.EqualTo(SmtWitnessStatus.Approximate));
        Assert.That(result.Witness.Assignments.Single().StringValue, Is.Not.Null);
    }

    [Test]
    public void SmtSolver_NonZeroGuardDoesNotImplyZero_IsSatisfiable()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xNotZero = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(0));
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

        AssertImplication(
            Feasibility.Satisfiable,
            new[] { xNotZero },
            xIsZero);
    }

    [Test]
    public void SmtSolver_ZeroGuardImpliesZero_IsUnsatisfiable()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[] { xIsZero },
            xIsZero);
    }

    [TestCase(SmtIntegerBinaryOperator.Divide)]
    [TestCase(SmtIntegerBinaryOperator.Remainder)]
    public void SmtSolver_UnresolvedDivisor_ReturnsUnknown(SmtIntegerBinaryOperator op)
    {
        var dividend = new SmtVariable("dividend", SmtValueKind.Int);
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var term = new SmtIntegerBinaryTerm(op, dividend, divisor);
        var contradiction = new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtBinaryFormula(SmtBinaryOperator.Equal, term, term));

        AssertSatisfiability(
            Feasibility.Unknown,
            new[] { contradiction });
    }

    [TestCase(SmtIntegerBinaryOperator.Divide)]
    [TestCase(SmtIntegerBinaryOperator.Remainder)]
    public void SmtSolver_DivisorRangeIncludingZero_ReturnsUnknown(SmtIntegerBinaryOperator op)
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var term = new SmtIntegerBinaryTerm(op, new SmtIntegerConstant(10), divisor);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    divisor,
                    new SmtIntegerConstant(-3)),
                new SmtBinaryFormula(
                    SmtBinaryOperator.LessThanOrEqual,
                    divisor,
                    new SmtIntegerConstant(3)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, term, new SmtIntegerConstant(2))
            });
    }

    [Test]
    public void SmtSolver_AffineEqualityAndConflictingInequality_IsUnsatisfiable()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(1));
        var affineEquality = new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusOne, new SmtIntegerConstant(0));
        var xIsNonNegative = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0));

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[] { affineEquality, xIsNonNegative });
    }

    [Test]
    public void SmtSolver_UnsupportedRegexWithoutConcreteInput_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "(")
            });
    }

    [Test]
    public void SmtSolver_UnsupportedRegexOptionsWithoutConcreteInput_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.IgnoreCase)
            });
    }

    [Test]
    public void SmtSolver_UnsupportedRegexOptionsConcreteMismatchUsesDotNetOptions()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.IgnoreCase),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("CD"))
            });
    }

    [Test]
    public void SmtSolver_MultilineOptionStrictAnchorsRemainExact()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var textIsAb = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            text,
            new SmtStringConstant("AB"));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[]
            {
                new SmtRegexMatchFormula(text, @"\AAB\z", RegexOptions.Multiline)
            },
            textIsAb);
    }

    [Test]
    public void SmtSolver_MultilineCaretAnchorWithoutConcreteInput_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "^AB", RegexOptions.Multiline)
            });
    }

    [Test]
    public void SmtSolver_LeadingContiguousAnchorRegexAcceptsInitialMatch()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\GAB"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_LeadingContiguousAnchorRegexContradictsLaterMatch()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\GAB"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("XAB"))
            });
    }

    [Test]
    public void SmtSolver_InternalContiguousAnchorRegexWithoutConcreteInput_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\AA\GB\z")
            });
    }

    [Test]
    public void SmtSolver_CultureInvariantIgnoreCaseRegexImpliesLiteralLength()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var lengthIsTwo = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(2));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[]
            {
                new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
            },
            lengthIsTwo);
    }

    [Test]
    public void SmtSolver_CultureInvariantIgnoreCaseRegexAcceptsCaseVariantLiteral()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\Aab\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_CultureInvariantIgnoreCaseCharClassAcceptsUppercaseVariant()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[a-c]\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("B"))
            });
    }

    [Test]
    public void SmtSolver_InvalidRegexCategoryWithoutConcreteInput_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\p{NotARealCategory}\z"),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_FinalNewlineRegexAnchorImpliesBoundedLength()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var boundedLength = new SmtBinaryFormula(
            SmtBinaryOperator.LessThanOrEqual,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(3));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[]
            {
                new SmtRegexMatchFormula(text, @"\AAB\Z")
            },
            boundedLength);
    }

    [Test]
    public void SmtSolver_InlineOptionBeforeStrictAnchorsImpliesLiteralLength()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var lengthIsTwo = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(2));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[]
            {
                new SmtRegexMatchFormula(text, @"(?i)\Aab\z", RegexOptions.CultureInvariant)
            },
            lengthIsTwo);
    }

    [Test]
    public void SmtSolver_IgnorePatternWhitespaceBeforeStrictStartAnchorSkipsTrivia()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var pattern = "(?x) # leading trivia\n \\A A B \\z";

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, pattern),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_InlineSinglelineBeforeCaretAnchorAllowsNewlineDot()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"(?s)^.\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n"))
            });
    }

    [Test]
    public void SmtSolver_InlineOptionBeforeDollarAnchorImpliesBoundedFinalNewlineLength()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var boundedLength = new SmtBinaryFormula(
            SmtBinaryOperator.LessThanOrEqual,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(2));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[]
            {
                new SmtRegexMatchFormula(text, "(?x)^ A $")
            },
            boundedLength);
    }

    [Test]
    public void SmtSolver_IgnorePatternWhitespaceGroupSkipsWhitespaceAndComments()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var pattern = "\\A(?x:A B # ignored comment\n C)\\z";

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, pattern),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("ABC"))
            });
    }

    [Test]
    public void SmtSolver_IgnorePatternWhitespaceGroupKeepsEscapedSpaceLiteral()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, "\\A(?x:A\\ B)\\z"),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("A B"))
            });
    }

    [Test]
    public void SmtSolver_DefaultDotRejectsNewline()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A.\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n"))
            });
    }

    [Test]
    public void SmtSolver_InlineSinglelineDotAllowsNewline()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?s:.)\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n"))
            });
    }

    [Test]
    public void SmtSolver_ScopedSinglelineDisableDotRejectsNewline()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?s:A(?-s:.)C)\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("A\nC"))
            });
    }

    [Test]
    public void SmtSolver_InlineIgnoreCaseOptionGroupAffectsFollowingLiterals()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?i)ab\z", RegexOptions.CultureInvariant),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_InlineIgnoreCaseDisableMakesFollowingLiteralCaseSensitive()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?i)ab(?-i)c\z", RegexOptions.CultureInvariant),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC"))
            });
    }

    [Test]
    public void SmtSolver_InlineIgnorePatternWhitespaceOptionSkipsRemainderTrivia()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var pattern = "\\A(?x)A B # ignored comment\n C\\z";

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, pattern),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("ABC"))
            });
    }

    [Test]
    public void SmtSolver_InlineSinglelineDisableMakesFollowingDotRejectsNewline()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?s).(?-s).\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("\n\n"))
            });
    }

    [Test]
    public void SmtSolver_InlineRegexCommentBeforeQuantifierPreservesPreviousAtom()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\AA(?# repeat previous atom)*\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AA"))
            });
    }

    [Test]
    public void SmtSolver_LeadingInlineRegexCommentBeforeStartAnchorKeepsAnchorStrict()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"(?# leading comment)\AAB\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("XAB"))
            });
    }

    [Test]
    public void SmtSolver_EscapedRegexClassLiteralContradictsPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[\.\]]\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            });
    }

    [Test]
    public void SmtSolver_LeadingBracketRegexClassLiteralContradictsPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[]]\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            });
    }

    [Test]
    public void SmtSolver_CharacterClassSubtractionRejectsExcludedLiteral()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[a-z-[aeiou]]\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("a"))
            });
    }

    [Test]
    public void SmtSolver_CharacterClassSubtractionAllowsRemainingLiteral()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[a-z-[aeiou]]\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("b"))
            });
    }

    [Test]
    public void SmtSolver_ControlCharacterEscapeAllowsExpectedCharacter()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\cA\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("\u0001")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_ControlCharacterClassEscapeContradictsDifferentCharacter()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[\cA]\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("\u0002")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_OctalRegexEscapeImpliesSpaceLiteral()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\040\z"),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant(" "))
            });
    }

    [Test]
    public void SmtSolver_OctalRegexEscapeConsumesAtMostTwoFollowingDigits()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\0408\z"),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant(" 8"))
            });
    }

    [Test]
    public void SmtSolver_OctalRegexClassEscapeContradictsDifferentCharacter()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[\040]\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            });
    }

    [Test]
    public void SmtSolver_PositiveLookaheadRegexContradictsImpossibleSuffix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?=AB)A.\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AC"))
            });
    }

    [Test]
    public void SmtSolver_PositiveLookaheadRegexAcceptsMatchingSuffix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?=AB)A.\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_NegativeLookaheadRegexRejectsExcludedSuffix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?!AB)A.\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_NegativeLookaheadRegexAcceptsDifferentSuffix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?!AB)A.\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AC"))
            });
    }

    [Test]
    public void SmtSolver_LookaheadWithoutConsumingSuffix_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\AA(?=B)")
            });
    }

    [Test]
    public void SmtSolver_PositiveLookbehindRegexContradictsImpossiblePrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<=AB)C\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AAC"))
            });
    }

    [Test]
    public void SmtSolver_PositiveLookbehindRegexAcceptsMatchingPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<=AB)C\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC"))
            });
    }

    [Test]
    public void SmtSolver_NegativeLookbehindRegexRejectsExcludedPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<!AB)C\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC"))
            });
    }

    [Test]
    public void SmtSolver_NegativeLookbehindRegexAcceptsDifferentPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[AB]{2}(?<!AB)C\z"),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("AAC"))
            });
    }

    [Test]
    public void SmtSolver_LookbehindWithoutParsedPrefix_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?<=A)B\z")
            });
    }

    [Test]
    public void SmtSolver_AtomicGroupRegexContradictsWrongPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?>A*)A\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("B"))
            });
    }

    [Test]
    public void SmtSolver_AtomicGroupApproximateSatisfiableResult_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A(?>A*)A\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            });
    }

    [Test]
    public void SmtSolver_NegatedApproximateRegexWithLength_ReturnsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    new SmtRegexMatchFormula(text, @"\A(?>A*)A\z")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_WordBoundaryRegexSatisfiableResult_IsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\bA\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            });
    }

    [Test]
    public void SmtSolver_WordBoundaryBetweenWordsIsUnsatisfiable()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\AA\bB\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_NonWordBoundaryBetweenWordsIsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\AA\BB\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_NonWordBoundaryBetweenWordAndPunctuationIsUnsatisfiable()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\AA\B!\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A!"))
            });
    }

    [Test]
    public void SmtSolver_DigitRegexContradictsNonDigitPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\d\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            });
    }

    [Test]
    public void SmtSolver_NonDigitRegexContradictsSingleDigitPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\D\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("5")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_NegatedDigitClassContradictsSingleDigitPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[^\d]\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("5")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_WhitespaceRegexContradictsNonWhitespacePrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\s\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            },
            TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public void SmtSolver_NonWhitespaceRegexContradictsNewlinePrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\S\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("\n")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            },
            TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public void SmtSolver_WordRegexContradictsPunctuationPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\w\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("!"))
            },
            TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public void SmtSolver_NonWordRegexContradictsUnderscorePrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\W\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("_")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            },
            TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public void SmtSolver_UnicodeCategoryRegexContradictsLetterPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\p{P}\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("A"))
            },
            TimeSpan.FromMilliseconds(250));
    }

    [TestCase(@"\A.\z")]
    [TestCase(@"\A\d\z")]
    [TestCase(@"\A\p{Lu}\z")]
    [TestCase(@"\A\P{Ll}\z")]
    [TestCase(@"\A\p{Lu}\P{Ll}\z")]
    public void SmtSolver_CharacterClassFallback_DoesNotRejectAValidLanguage(string pattern)
    {
        using var solver = new SmtSolver();
        var text = new SmtVariable("text", SmtValueKind.String);

        var result = solver.CheckSatisfiability(
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, pattern)
            },
            TimeSpan.FromMilliseconds(250)).Feasibility;

        Assert.That(result, Is.Not.EqualTo(Feasibility.Unsatisfiable));
    }

    [Test]
    public void SmtSolver_NegatedUnicodeCategoryRegexContradictsPunctuationPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A\P{P}\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("!")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            },
            TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public void SmtSolver_NegatedUnicodeCategoryClassContradictsPunctuationPrefix()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtRegexMatchFormula(text, @"\A[^\p{P}]\z"),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("!")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(1))
            },
            TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public void SmtSolver_LargeUnicodeCategoryConclusionDoesNotBecomeProof()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var lengthIsOne = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(1));
        var textIsUppercaseLetter = new SmtRegexMatchFormula(text, @"\A\p{Lu}\z");

        AssertImplication(
            Feasibility.Unknown,
            new[] { lengthIsOne },
            textIsUppercaseLetter,
            TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public void SmtSolver_WordBoundaryRegexPathProvesLengthImplication()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var lengthIsOne = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(1));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[]
            {
                new SmtRegexMatchFormula(text, @"\A\bA\z")
            },
            lengthIsOne);
    }

    [Test]
    public void SmtSolver_WordBoundaryRegexConclusionRemainsUnknown()
    {
        var text = new SmtVariable("text", SmtValueKind.String);
        var lengthIsOne = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtStringLengthTerm(text),
            new SmtIntegerConstant(1));
        var textIsBoundaryA = new SmtRegexMatchFormula(text, @"\A\bA\z");

        AssertImplication(
            Feasibility.Unknown,
            new[] { lengthIsOne },
            textIsBoundaryA);
    }

    [Test]
    public void SmtSolver_NonPositiveTimeout_ReturnsUnknown()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(1))
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_MismatchedEqualitySorts_ReturnsUnknown()
    {
        var intValue = new SmtVariable("mixed", SmtValueKind.Int);
        var stringValue = new SmtVariable("mixed", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, intValue, stringValue)
            });
    }

    [Test]
    public void SmtSolver_ConditionalIntegerTermHonorsSelectedBranch()
    {
        var useFirstBranch = new SmtVariable("useFirstBranch", SmtValueKind.Bool);
        var selectedValue = new SmtConditionalFormula(
            useFirstBranch,
            new SmtIntegerConstant(1),
            new SmtIntegerConstant(2),
            SmtValueKind.Int);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                useFirstBranch,
                new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    selectedValue,
                    new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_AffineGuardImpliesExactValue_IsUnsatisfiable()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xMinusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, x, new SmtIntegerConstant(1));
        var guard = new SmtBinaryFormula(SmtBinaryOperator.Equal, xMinusOne, new SmtIntegerConstant(0));
        var conclusion = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(1));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[] { guard },
            conclusion);
    }

    [Test]
    public void SmtSolver_MultiplicationByConstantContradictsRange()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var twiceX = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, x, new SmtIntegerConstant(2));

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(5)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, twiceX, new SmtIntegerConstant(10))
            });
    }

    [Test]
    public void SmtSolver_StringPrefixSuffixAndLengthFactsCombine()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                new SmtStringEndsWithFormula(text, new SmtStringConstant("CD")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(3))
            });
    }

    [Test]
    public void SmtSolver_StringContainsAndConcatFactsCombine()
    {
        var left = new SmtVariable("left", SmtValueKind.String);
        var right = new SmtVariable("right", SmtValueKind.String);
        var combined = new SmtStringConcatTerm(left, right);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, left, new SmtStringConstant("A")),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, right, new SmtStringConstant("B")),
                new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    new SmtStringContainsFormula(combined, new SmtStringConstant("AB")))
            });
    }

    [Test]
    public void SmtSolver_StringContainsLongerThanKnownLength_IsUnsatisfiable()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(2)),
                new SmtStringContainsFormula(text, new SmtStringConstant("ABC"))
            });
    }

    [Test]
    public void SmtSolver_StringContainsWithExactLengthInfersValue()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(2)),
                new SmtStringContainsFormula(text, new SmtStringConstant("AB")),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("AB"))
            });
    }

    [Test]
    public void SmtSolver_NegativeStringLengthBound_IsUnsatisfiable()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.LessThan,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(0))
            });
    }

    [Test]
    public void SmtSolver_DivideByConcreteZero_ReturnsUnknown()
    {
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            new SmtIntegerConstant(0));

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(0))
            });
    }

    [Test]
    public void SmtSolver_DivideByZeroFromEquality_ReturnsUnknown()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            divisor);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(0))
            });
    }

    [Test]
    public void SmtSolver_DivisionWithNonZeroGuard_RemainsUsable()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            divisor);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(2)),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, division, new SmtIntegerConstant(5))
            });
    }

    [Test]
    public void SmtSolver_DivisionWithExplicitNonZeroGuard_RemainsUsable()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            divisor);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    divisor,
                    new SmtIntegerConstant(0)),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    division,
                    new SmtIntegerConstant(5))
            });
    }

    [Test]
    public void SmtSolver_StrictBoundBeyondInt64Range_IsUnsatisfiableBeforeDivision()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            divisor);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThan,
                    divisor,
                    new SmtIntegerConstant(long.MaxValue)),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    division,
                    new SmtIntegerConstant(0))
            });
    }

    [Test]
    public void SmtSolver_RemainderWithRelationalNonZeroGuard_RemainsUsable()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var remainder = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Remainder,
            new SmtIntegerConstant(5),
            divisor);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, divisor, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, divisor, new SmtIntegerConstant(2)),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, remainder, new SmtIntegerConstant(1))
            });
    }

    [Test]
    public void SmtSolver_NegativeDividendDivision_UsesCSharpTruncation()
    {
        var dividend = new SmtVariable("dividend", SmtValueKind.Int);
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            dividend,
            new SmtIntegerConstant(2));

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, dividend, new SmtIntegerConstant(-2)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, dividend, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(0))
            });
    }

    [Test]
    public void SmtSolver_NegativeDividendRemainder_UsesCSharpSign()
    {
        var dividend = new SmtVariable("dividend", SmtValueKind.Int);
        var remainder = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Remainder,
            dividend,
            new SmtIntegerConstant(2));

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, dividend, new SmtIntegerConstant(-2)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, dividend, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, remainder, new SmtIntegerConstant(-1))
            });
    }

    [Test]
    public void SmtSolver_NegativeDivisorDivision_UsesCSharpTruncation()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(3),
            divisor);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, divisor, new SmtIntegerConstant(-3)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, divisor, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(-1))
            });
    }

    [Test]
    public void SmtSolver_NonNegativeRemainderWithPositiveDivisor_ProvesRange()
    {
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

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, dividend, new SmtIntegerConstant(0)),
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, divisor, new SmtIntegerConstant(0)),
                outOfRange
            });
    }

    [Test]
    public void SmtSolver_SelectedConditionalSkipsUnsafeUnselectedIntegerBranch()
    {
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

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                useSafeBranch,
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, selectedValue, new SmtIntegerConstant(7))
            });
    }

    [Test]
    public void SmtSolver_NegatedComparisonGuardKeepsDivisionUsable()
    {
        var divisor = new SmtVariable("divisor", SmtValueKind.Int);
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            divisor);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    new SmtBinaryFormula(
                        SmtBinaryOperator.LessThanOrEqual,
                        divisor,
                        new SmtIntegerConstant(0))),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, division, new SmtIntegerConstant(999))
            });
    }

    [Test]
    public void SmtSolver_AffineRangeGuardKeepsDerivedDivisorUsable()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var divisor = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Subtract,
            x,
            new SmtIntegerConstant(1));
        var division = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            divisor);

        AssertSatisfiability(
            Feasibility.Satisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(1)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, division, new SmtIntegerConstant(10))
            });
    }

    [Test]
    public void SmtSolver_AffineEqualityPropagatesSolvedValue()
    {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xPlusTwo = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Add,
            x,
            new SmtIntegerConstant(2));

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusTwo, new SmtIntegerConstant(5)),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(3))
            });
    }

    [Test]
    public void SmtSolver_ConflictingStringPrefixesAreUnsatisfiable()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AC"))
            });
    }

    [Test]
    public void SmtSolver_ExactLengthPrefixSuffixOverlapInfersString()
    {
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtStringStartsWithFormula(text, new SmtStringConstant("AB")),
                new SmtStringEndsWithFormula(text, new SmtStringConstant("BC")),
                new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtStringLengthTerm(text),
                    new SmtIntegerConstant(3)),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, text, new SmtStringConstant("ABC"))
            });
    }

    [Test]
    public void SmtSolver_SelectedConditionalStringConcatSimplifiesPredicate()
    {
        var usePrefix = new SmtVariable("usePrefix", SmtValueKind.Bool);
        var selected = new SmtConditionalFormula(
            usePrefix,
            new SmtStringConstant("AB"),
            new SmtStringConstant("CD"),
            SmtValueKind.String);
        var combined = new SmtStringConcatTerm(selected, new SmtStringConstant("X"));

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                usePrefix,
                new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    new SmtStringStartsWithFormula(combined, new SmtStringConstant("AB")))
            });
    }

    [Test]
    public void SmtSolver_BooleanAliasChainContradiction_IsPreprocessedWithoutZ3()
    {
        var first = new SmtVariable("first", SmtValueKind.Bool);
        var second = new SmtVariable("second", SmtValueKind.Bool);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, first, second),
                first,
                new SmtUnaryFormula(SmtUnaryOperator.Not, second)
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_IntegerAliasIntervalContradiction_IsPreprocessedWithoutZ3()
    {
        var alias = new SmtVariable("alias", SmtValueKind.Int);
        var source = new SmtVariable("source", SmtValueKind.Int);
        var aliasPlusOne = new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Add,
            alias,
            new SmtIntegerConstant(1));

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, alias, source),
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, source, new SmtIntegerConstant(5)),
                new SmtBinaryFormula(SmtBinaryOperator.LessThan, aliasPlusOne, new SmtIntegerConstant(5))
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_StringAliasPredicateContradiction_IsPreprocessedWithoutZ3()
    {
        var copy = new SmtVariable("copy", SmtValueKind.String);
        var text = new SmtVariable("text", SmtValueKind.String);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, copy, text),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, text, new SmtStringConstant("ABC")),
                new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    new SmtStringStartsWithFormula(copy, new SmtStringConstant("A")))
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_ReferenceAliasNullContradiction_IsPreprocessedWithoutZ3()
    {
        var source = new SmtVariable("source", SmtValueKind.Reference);
        var target = new SmtVariable("target", SmtValueKind.Reference);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.Equal, target, source),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, source, new SmtNullConstant()),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, target, new SmtNullConstant())
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_DistinctNonNullReferencesRemainUnknownWithoutZ3()
    {
        var left = new SmtVariable("left", SmtValueKind.Reference);
        var right = new SmtVariable("right", SmtValueKind.Reference);

        AssertSatisfiability(
            Feasibility.Unknown,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, new SmtNullConstant()),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, right, new SmtNullConstant()),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right)
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_IdenticalConditionalBranchesSimplifyWithoutZ3()
    {
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

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, selected, valuePlusOne)
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_ConcatLengthNegativeComparison_IsPreprocessedWithoutZ3()
    {
        var left = new SmtVariable("left", SmtValueKind.String);
        var right = new SmtVariable("right", SmtValueKind.String);
        var combined = new SmtStringConcatTerm(left, right);

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBinaryFormula(
                    SmtBinaryOperator.LessThan,
                    new SmtStringLengthTerm(combined),
                    new SmtIntegerConstant(0))
            },
            TimeSpan.Zero);
    }

    [Test]
    public void SmtSolver_ReferenceNullAndNonNullConjunction_IsUnsatisfiable()
    {
        var reference = new SmtVariable("reference", SmtValueKind.Reference);
        var isNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, reference, new SmtNullConstant());
        var isNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, reference, new SmtNullConstant());

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[] { isNull, isNotNull });
    }

    [Test]
    public void PurityProof_NonNullGuard_MakesNullDereferenceProvablyPure()
    {
        using var search = new PurityProofSearch();
        var s = new SmtVariable("s", SmtValueKind.Reference);
        var sIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, s, new SmtNullConstant());
        var sIsNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, s, new SmtNullConstant());

        var result = search.Classify(
            new PurityProofQuery(
                new[] { sIsNotNull },
                new PurityHazard(PurityHazardKind.NullDereference, sIsNull)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(result.Reason, Is.EqualTo("null_dereference_unreachable"));
    }
}
