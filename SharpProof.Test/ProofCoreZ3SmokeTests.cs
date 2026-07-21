using System.Text.RegularExpressions;
using NUnit.Framework;
using SharpProof.ProofCore.Analysis;
using SharpProof.ProofCore.Smt;
using static SharpProof.Test.SmtTestFormula;

namespace SharpProof.Test;

[TestFixture]
internal class ProofCoreZ3SmokeTests {
    private static readonly TimeSpan SolverTimeout = TimeSpan.FromMilliseconds(50);

    [TestCase("^[a-z]+$", RegexTranslationFallback.None)]
    [TestCase("[", RegexTranslationFallback.InvalidPattern)]
    public void RegexTranslationValidator_ClassifiesInput(string pattern, RegexTranslationFallback expected) {
        Assert.That(Z3RegexTranslationValidator.Validate(pattern, RegexOptions.CultureInvariant), Is.EqualTo(expected));
    }

    [Test]
    public void RegexTranslationValidator_ClassifiesOversizedPatternBeforeParsing() {
        Assert.That(
            Z3RegexTranslationValidator.Validate(new string('a', 257), RegexOptions.None),
            Is.EqualTo(RegexTranslationFallback.PatternTooLong));
    }

    private static void AssertSatisfiability(
        Feasibility expected,
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan? timeout = null) {
        using var solver = new SmtSolver();
        Assert.That(
            solver.CheckSatisfiability(pathConditions, timeout ?? SolverTimeout).Feasibility,
            Is.EqualTo(expected));
    }

    private static void AssertImplication(
        Feasibility expected,
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula conclusion,
        TimeSpan? timeout = null) {
        using var solver = new SmtSolver();
        Assert.That(
            solver.CheckSatisfiability(
                    pathConditions.Concat(new[] { new SmtUnaryFormula(SmtUnaryOperator.Not, conclusion) }),
                    timeout ?? SolverTimeout)
                .Feasibility,
            Is.EqualTo(expected));
    }

    [Test]
    public void SmtSolver_TrueAndFalseConjunction_IsUnsatisfiable() {
        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[]
            {
                new SmtBooleanConstant(true),
                new SmtBooleanConstant(false)
            });
    }

    [Test]
    public void SmtSolver_CheckSatisfiability_ExposesTypedExactAssignments() {
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
        Assert.Multiple(() => {
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
    public void SmtSolver_CheckSatisfiability_ExposesRangeModel() {
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
    public void SmtSolver_CheckSatisfiability_MarksOpaqueReferenceModelApproximate() {
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
    public void SmtSolver_CheckSatisfiability_PreservesApproximateRegexCandidateModel() {
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
    public void SmtSolver_NonZeroGuardDoesNotImplyZero_IsSatisfiable() {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xNotZero = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(0));
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

        AssertImplication(
            Feasibility.Satisfiable,
            new[] { xNotZero },
            xIsZero);
    }

    [Test]
    public void SmtSolver_ZeroGuardImpliesZero_IsUnsatisfiable() {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

        AssertImplication(
            Feasibility.Unsatisfiable,
            new[] { xIsZero },
            xIsZero);
    }

    [TestCase(SmtIntegerBinaryOperator.Divide)]
    [TestCase(SmtIntegerBinaryOperator.Remainder)]
    public void SmtSolver_UnresolvedDivisor_ReturnsUnknown(SmtIntegerBinaryOperator op) {
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
    public void SmtSolver_DivisorRangeIncludingZero_ReturnsUnknown(SmtIntegerBinaryOperator op) {
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
    public void SmtSolver_AffineEqualityAndConflictingInequality_IsUnsatisfiable() {
        var x = new SmtVariable("x", SmtValueKind.Int);
        var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(1));
        var affineEquality = new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusOne, new SmtIntegerConstant(0));
        var xIsNonNegative = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0));

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[] { affineEquality, xIsNonNegative });
    }

    public enum RegexConstraint { None, Equal, NotEqual, StartsWith, LengthEqual, StartsWithAndLength, NegatedMatchAndLength }

    public enum RegexConclusion { Equal, LengthEqual, LengthAtMost }

    public sealed record RegexCase(
        Feasibility Expected,
        string Pattern,
        RegexConstraint Constraint = RegexConstraint.None,
        string? Value = null,
        int Length = 0,
        RegexOptions Options = RegexOptions.None,
        bool ExtendedTimeout = false);

    public sealed record RegexImplicationCase(
        string Pattern,
        RegexConclusion Conclusion,
        string? Value = null,
        int Length = 0,
        RegexOptions Options = RegexOptions.None);

    private static IEnumerable<TestCaseData> RegexSatisfiabilityCases() {
        yield return CreateRegexCase("SmtSolver_UnsupportedRegexWithoutConcreteInput_ReturnsUnknown", Feasibility.Unknown, "(");
        yield return CreateRegexCase("SmtSolver_UnsupportedRegexOptionsWithoutConcreteInput_ReturnsUnknown", Feasibility.Unknown, @"\Aab\z", options: RegexOptions.IgnoreCase);
        yield return CreateRegexCase("SmtSolver_UnsupportedRegexOptionsConcreteMismatchUsesDotNetOptions", Feasibility.Unsatisfiable, @"\Aab\z", RegexConstraint.Equal, "CD", options: RegexOptions.IgnoreCase);
        yield return CreateRegexCase("SmtSolver_MultilineCaretAnchorWithoutConcreteInput_ReturnsUnknown", Feasibility.Unknown, "^AB", options: RegexOptions.Multiline);
        yield return CreateRegexCase("SmtSolver_LeadingContiguousAnchorRegexAcceptsInitialMatch", Feasibility.Satisfiable, @"\GAB", RegexConstraint.StartsWith, "AB");
        yield return CreateRegexCase("SmtSolver_LeadingContiguousAnchorRegexContradictsLaterMatch", Feasibility.Unsatisfiable, @"\GAB", RegexConstraint.StartsWith, "XAB");
        yield return CreateRegexCase("SmtSolver_InternalContiguousAnchorRegexWithoutConcreteInput_ReturnsUnknown", Feasibility.Unknown, @"\AA\GB\z");
        yield return CreateRegexCase("SmtSolver_CultureInvariantIgnoreCaseRegexAcceptsCaseVariantLiteral", Feasibility.Satisfiable, @"\Aab\z", RegexConstraint.Equal, "AB", options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        yield return CreateRegexCase("SmtSolver_CultureInvariantIgnoreCaseCharClassAcceptsUppercaseVariant", Feasibility.Satisfiable, @"\A[a-c]\z", RegexConstraint.Equal, "B", options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        yield return CreateRegexCase("SmtSolver_InvalidRegexCategoryWithoutConcreteInput_ReturnsUnknown", Feasibility.Unknown, @"\A\p{NotARealCategory}\z", RegexConstraint.LengthEqual, length: 1);
        yield return CreateRegexCase("SmtSolver_IgnorePatternWhitespaceBeforeStrictStartAnchorSkipsTrivia", Feasibility.Unsatisfiable, "(?x) # leading trivia\n \\A A B \\z", RegexConstraint.NotEqual, "AB");
        yield return CreateRegexCase("SmtSolver_InlineSinglelineBeforeCaretAnchorAllowsNewlineDot", Feasibility.Satisfiable, @"(?s)^.\z", RegexConstraint.Equal, "\n");
        yield return CreateRegexCase("SmtSolver_IgnorePatternWhitespaceGroupSkipsWhitespaceAndComments", Feasibility.Unsatisfiable, "\\A(?x:A B # ignored comment\n C)\\z", RegexConstraint.NotEqual, "ABC");
        yield return CreateRegexCase("SmtSolver_IgnorePatternWhitespaceGroupKeepsEscapedSpaceLiteral", Feasibility.Unsatisfiable, "\\A(?x:A\\ B)\\z", RegexConstraint.NotEqual, "A B");
        yield return CreateRegexCase("SmtSolver_DefaultDotRejectsNewline", Feasibility.Unsatisfiable, @"\A.\z", RegexConstraint.Equal, "\n");
        yield return CreateRegexCase("SmtSolver_InlineSinglelineDotAllowsNewline", Feasibility.Satisfiable, @"\A(?s:.)\z", RegexConstraint.Equal, "\n");
        yield return CreateRegexCase("SmtSolver_ScopedSinglelineDisableDotRejectsNewline", Feasibility.Unsatisfiable, @"\A(?s:A(?-s:.)C)\z", RegexConstraint.Equal, "A\nC");
        yield return CreateRegexCase("SmtSolver_InlineIgnoreCaseOptionGroupAffectsFollowingLiterals", Feasibility.Satisfiable, @"\A(?i)ab\z", RegexConstraint.Equal, "AB", options: RegexOptions.CultureInvariant);
        yield return CreateRegexCase("SmtSolver_InlineIgnoreCaseDisableMakesFollowingLiteralCaseSensitive", Feasibility.Unsatisfiable, @"\A(?i)ab(?-i)c\z", RegexConstraint.Equal, "ABC", options: RegexOptions.CultureInvariant);
        yield return CreateRegexCase("SmtSolver_InlineIgnorePatternWhitespaceOptionSkipsRemainderTrivia", Feasibility.Unsatisfiable, "\\A(?x)A B # ignored comment\n C\\z", RegexConstraint.NotEqual, "ABC");
        yield return CreateRegexCase("SmtSolver_InlineSinglelineDisableMakesFollowingDotRejectsNewline", Feasibility.Unsatisfiable, @"\A(?s).(?-s).\z", RegexConstraint.Equal, "\n\n");
        yield return CreateRegexCase("SmtSolver_InlineRegexCommentBeforeQuantifierPreservesPreviousAtom", Feasibility.Satisfiable, @"\AA(?# repeat previous atom)*\z", RegexConstraint.Equal, "AA");
        yield return CreateRegexCase("SmtSolver_LeadingInlineRegexCommentBeforeStartAnchorKeepsAnchorStrict", Feasibility.Unsatisfiable, @"(?# leading comment)\AAB\z", RegexConstraint.StartsWith, "XAB");
        yield return CreateRegexCase("SmtSolver_EscapedRegexClassLiteralContradictsPrefix", Feasibility.Unsatisfiable, @"\A[\.\]]\z", RegexConstraint.StartsWith, "A");
        yield return CreateRegexCase("SmtSolver_LeadingBracketRegexClassLiteralContradictsPrefix", Feasibility.Unsatisfiable, @"\A[]]\z", RegexConstraint.StartsWith, "A");
        yield return CreateRegexCase("SmtSolver_CharacterClassSubtractionRejectsExcludedLiteral", Feasibility.Unsatisfiable, @"\A[a-z-[aeiou]]\z", RegexConstraint.Equal, "a");
        yield return CreateRegexCase("SmtSolver_CharacterClassSubtractionAllowsRemainingLiteral", Feasibility.Satisfiable, @"\A[a-z-[aeiou]]\z", RegexConstraint.Equal, "b");
        yield return CreateRegexCase("SmtSolver_ControlCharacterEscapeAllowsExpectedCharacter", Feasibility.Satisfiable, @"\A\cA\z", RegexConstraint.StartsWithAndLength, "\u0001", 1);
        yield return CreateRegexCase("SmtSolver_ControlCharacterClassEscapeContradictsDifferentCharacter", Feasibility.Unsatisfiable, @"\A[\cA]\z", RegexConstraint.StartsWithAndLength, "\u0002", 1);
        yield return CreateRegexCase("SmtSolver_OctalRegexEscapeImpliesSpaceLiteral", Feasibility.Unsatisfiable, @"\A\040\z", RegexConstraint.NotEqual, " ");
        yield return CreateRegexCase("SmtSolver_OctalRegexEscapeConsumesAtMostTwoFollowingDigits", Feasibility.Unsatisfiable, @"\A\0408\z", RegexConstraint.NotEqual, " 8");
        yield return CreateRegexCase("SmtSolver_OctalRegexClassEscapeContradictsDifferentCharacter", Feasibility.Unsatisfiable, @"\A[\040]\z", RegexConstraint.StartsWith, "A");
        yield return CreateRegexCase("SmtSolver_PositiveLookaheadRegexContradictsImpossibleSuffix", Feasibility.Unsatisfiable, @"\A(?=AB)A.\z", RegexConstraint.Equal, "AC");
        yield return CreateRegexCase("SmtSolver_PositiveLookaheadRegexAcceptsMatchingSuffix", Feasibility.Satisfiable, @"\A(?=AB)A.\z", RegexConstraint.Equal, "AB");
        yield return CreateRegexCase("SmtSolver_NegativeLookaheadRegexRejectsExcludedSuffix", Feasibility.Unsatisfiable, @"\A(?!AB)A.\z", RegexConstraint.Equal, "AB");
        yield return CreateRegexCase("SmtSolver_NegativeLookaheadRegexAcceptsDifferentSuffix", Feasibility.Satisfiable, @"\A(?!AB)A.\z", RegexConstraint.Equal, "AC");
        yield return CreateRegexCase("SmtSolver_LookaheadWithoutConsumingSuffix_ReturnsUnknown", Feasibility.Unknown, @"\AA(?=B)");
        yield return CreateRegexCase("SmtSolver_PositiveLookbehindRegexContradictsImpossiblePrefix", Feasibility.Unsatisfiable, @"\A[AB]{2}(?<=AB)C\z", RegexConstraint.Equal, "AAC");
        yield return CreateRegexCase("SmtSolver_PositiveLookbehindRegexAcceptsMatchingPrefix", Feasibility.Satisfiable, @"\A[AB]{2}(?<=AB)C\z", RegexConstraint.Equal, "ABC");
        yield return CreateRegexCase("SmtSolver_NegativeLookbehindRegexRejectsExcludedPrefix", Feasibility.Unsatisfiable, @"\A[AB]{2}(?<!AB)C\z", RegexConstraint.Equal, "ABC");
        yield return CreateRegexCase("SmtSolver_NegativeLookbehindRegexAcceptsDifferentPrefix", Feasibility.Satisfiable, @"\A[AB]{2}(?<!AB)C\z", RegexConstraint.Equal, "AAC");
        yield return CreateRegexCase("SmtSolver_LookbehindWithoutParsedPrefix_ReturnsUnknown", Feasibility.Unknown, @"\A(?<=A)B\z");
        yield return CreateRegexCase("SmtSolver_AtomicGroupRegexContradictsWrongPrefix", Feasibility.Unsatisfiable, @"\A(?>A*)A\z", RegexConstraint.StartsWith, "B");
        yield return CreateRegexCase("SmtSolver_AtomicGroupApproximateSatisfiableResult_ReturnsUnknown", Feasibility.Unknown, @"\A(?>A*)A\z", RegexConstraint.StartsWith, "A");
        yield return CreateRegexCase("SmtSolver_NegatedApproximateRegexWithLength_ReturnsUnknown", Feasibility.Unknown, @"\A(?>A*)A\z", RegexConstraint.NegatedMatchAndLength, length: 1);
        yield return CreateRegexCase("SmtSolver_WordBoundaryRegexSatisfiableResult_IsUnknown", Feasibility.Unknown, @"\A\bA\z", RegexConstraint.StartsWith, "A");
        yield return CreateRegexCase("SmtSolver_WordBoundaryBetweenWordsIsUnsatisfiable", Feasibility.Unsatisfiable, @"\AA\bB\z", RegexConstraint.StartsWith, "AB");
        yield return CreateRegexCase("SmtSolver_NonWordBoundaryBetweenWordsIsUnknown", Feasibility.Unknown, @"\AA\BB\z", RegexConstraint.StartsWith, "AB");
        yield return CreateRegexCase("SmtSolver_NonWordBoundaryBetweenWordAndPunctuationIsUnsatisfiable", Feasibility.Unsatisfiable, @"\AA\B!\z", RegexConstraint.StartsWith, "A!");
        yield return CreateRegexCase("SmtSolver_DigitRegexContradictsNonDigitPrefix", Feasibility.Unsatisfiable, @"\A\d\z", RegexConstraint.StartsWith, "A");
        yield return CreateRegexCase("SmtSolver_NonDigitRegexContradictsSingleDigitPrefix", Feasibility.Unsatisfiable, @"\A\D\z", RegexConstraint.StartsWithAndLength, "5", 1);
        yield return CreateRegexCase("SmtSolver_NegatedDigitClassContradictsSingleDigitPrefix", Feasibility.Unsatisfiable, @"\A[^\d]\z", RegexConstraint.StartsWithAndLength, "5", 1);
        yield return CreateRegexCase("SmtSolver_WhitespaceRegexContradictsNonWhitespacePrefix", Feasibility.Unsatisfiable, @"\A\s\z", RegexConstraint.StartsWith, "A", extendedTimeout: true);
        yield return CreateRegexCase("SmtSolver_NonWhitespaceRegexContradictsNewlinePrefix", Feasibility.Unsatisfiable, @"\A\S\z", RegexConstraint.StartsWithAndLength, "\n", 1, extendedTimeout: true);
        yield return CreateRegexCase("SmtSolver_WordRegexContradictsPunctuationPrefix", Feasibility.Unsatisfiable, @"\A\w\z", RegexConstraint.StartsWith, "!", extendedTimeout: true);
        yield return CreateRegexCase("SmtSolver_NonWordRegexContradictsUnderscorePrefix", Feasibility.Unsatisfiable, @"\A\W\z", RegexConstraint.StartsWithAndLength, "_", 1, extendedTimeout: true);
        yield return CreateRegexCase("SmtSolver_UnicodeCategoryRegexContradictsLetterPrefix", Feasibility.Unsatisfiable, @"\A\p{P}\z", RegexConstraint.StartsWith, "A", extendedTimeout: true);
    }

    private static IEnumerable<TestCaseData> RegexImplicationCases() {
        yield return CreateRegexImplicationCase("SmtSolver_MultilineOptionStrictAnchorsRemainExact", @"\AAB\z", RegexConclusion.Equal, "AB", options: RegexOptions.Multiline);
        yield return CreateRegexImplicationCase("SmtSolver_CultureInvariantIgnoreCaseRegexImpliesLiteralLength", @"\Aab\z", RegexConclusion.LengthEqual, length: 2, options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        yield return CreateRegexImplicationCase("SmtSolver_FinalNewlineRegexAnchorImpliesBoundedLength", @"\AAB\Z", RegexConclusion.LengthAtMost, length: 3);
        yield return CreateRegexImplicationCase("SmtSolver_InlineOptionBeforeStrictAnchorsImpliesLiteralLength", @"(?i)\Aab\z", RegexConclusion.LengthEqual, length: 2, options: RegexOptions.CultureInvariant);
        yield return CreateRegexImplicationCase("SmtSolver_InlineOptionBeforeDollarAnchorImpliesBoundedFinalNewlineLength", "(?x)^ A $", RegexConclusion.LengthAtMost, length: 2);
    }

    [TestCaseSource(nameof(RegexSatisfiabilityCases))]
    public void SmtSolver_RegexSatisfiabilityMatrix(RegexCase testCase) {
        var text = new SmtVariable("text", SmtValueKind.String);
        var match = new SmtRegexMatchFormula(text, testCase.Pattern, testCase.Options);
        var length = new SmtStringLengthTerm(text);
        var formulas = testCase.Constraint switch {
            RegexConstraint.None => new SmtFormula[] { match },
            RegexConstraint.Equal => [match, Compare(SmtBinaryOperator.Equal, text, testCase.Value!)],
            RegexConstraint.NotEqual => [match, Compare(SmtBinaryOperator.NotEqual, text, testCase.Value!)],
            RegexConstraint.StartsWith => [match, RegexStartsWith(text, testCase.Value!)],
            RegexConstraint.LengthEqual => [match, Compare(SmtBinaryOperator.Equal, length, testCase.Length)],
            RegexConstraint.StartsWithAndLength =>
                [match, RegexStartsWith(text, testCase.Value!), Compare(SmtBinaryOperator.Equal, length, testCase.Length)],
            RegexConstraint.NegatedMatchAndLength =>
                [new SmtUnaryFormula(SmtUnaryOperator.Not, match),
                    Compare(SmtBinaryOperator.Equal, length, testCase.Length)],
            _ => throw new ArgumentOutOfRangeException()
        };
        AssertSatisfiability(testCase.Expected, formulas,
            testCase.ExtendedTimeout ? TimeSpan.FromMilliseconds(250) : null);
    }

    [TestCaseSource(nameof(RegexImplicationCases))]
    public void SmtSolver_RegexImplicationMatrix(RegexImplicationCase testCase) {
        var text = new SmtVariable("text", SmtValueKind.String);
        var conclusion = testCase.Conclusion switch {
            RegexConclusion.Equal => Compare(SmtBinaryOperator.Equal, text, testCase.Value!),
            RegexConclusion.LengthEqual => Compare(
                SmtBinaryOperator.Equal, new SmtStringLengthTerm(text), testCase.Length),
            RegexConclusion.LengthAtMost => Compare(
                SmtBinaryOperator.LessThanOrEqual, new SmtStringLengthTerm(text), testCase.Length),
            _ => throw new ArgumentOutOfRangeException()
        };
        AssertImplication(Feasibility.Unsatisfiable,
            [new SmtRegexMatchFormula(text, testCase.Pattern, testCase.Options)], conclusion);
    }

    private static TestCaseData CreateRegexCase(
        string name,
        Feasibility expected,
        string pattern,
        RegexConstraint constraint = RegexConstraint.None,
        string? value = null,
        int length = 0,
        RegexOptions options = RegexOptions.None,
        bool extendedTimeout = false) => new TestCaseData(
        new RegexCase(expected, pattern, constraint, value, length, options, extendedTimeout)).SetName(name);

    private static TestCaseData CreateRegexImplicationCase(
        string name,
        string pattern,
        RegexConclusion conclusion,
        string? value = null,
        int length = 0,
        RegexOptions options = RegexOptions.None) => new TestCaseData(
        new RegexImplicationCase(pattern, conclusion, value, length, options)).SetName(name);

    private static SmtBinaryFormula Compare(SmtBinaryOperator operation, SmtFormula left, string right) =>
        new(operation, left, new SmtStringConstant(right));

    private static SmtBinaryFormula Compare(SmtBinaryOperator operation, SmtFormula left, int right) =>
        new(operation, left, new SmtIntegerConstant(right));

    private static SmtStringStartsWithFormula RegexStartsWith(SmtFormula text, string prefix) =>
        new(text, new SmtStringConstant(prefix));
    [TestCase(@"\A.\z")]
    [TestCase(@"\A\d\z")]
    [TestCase(@"\A\p{Lu}\z")]
    [TestCase(@"\A\P{Ll}\z")]
    [TestCase(@"\A\p{Lu}\P{Ll}\z")]
    public void SmtSolver_CharacterClassFallback_DoesNotRejectAValidLanguage(string pattern) {
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
    public void SmtSolver_NegatedUnicodeCategoryRegexContradictsPunctuationPrefix() {
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
    public void SmtSolver_NegatedUnicodeCategoryClassContradictsPunctuationPrefix() {
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
    public void SmtSolver_LargeUnicodeCategoryConclusionDoesNotBecomeProof() {
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
    public void SmtSolver_WordBoundaryRegexPathProvesLengthImplication() {
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
    public void SmtSolver_WordBoundaryRegexConclusionRemainsUnknown() {
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

    private sealed record SolverCase(
        SmtFormula[] Conditions,
        Feasibility Expected,
        SmtFormula? Conclusion = null,
        bool ZeroTimeout = false);

    private static IEnumerable<TestCaseData> SolverCases() {
        yield return CreateSolverCase("SmtSolver_NonPositiveTimeout_ReturnsUnknown", [Equal(Int("x"), Integer(1))], Feasibility.Unknown, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_MismatchedEqualitySorts_ReturnsUnknown", [Equal(Int("mixed"), String("mixed"))], Feasibility.Unknown);
        yield return CreateSolverCase("SmtSolver_ConditionalIntegerTermHonorsSelectedBranch",
            [Bool("useFirstBranch"), NotEqual(Conditional(Bool("useFirstBranch"), Integer(1), Integer(2), SmtValueKind.Int), Integer(1))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_AffineGuardImpliesExactValue_IsUnsatisfiable",
            [Equal(Subtract(Int("x"), Integer(1)), Integer(0))], Feasibility.Unsatisfiable, Equal(Int("x"), Integer(1)));
        yield return CreateSolverCase("SmtSolver_MultiplicationByConstantContradictsRange",
            [GreaterThanOrEqual(Int("x"), Integer(5)), LessThan(Multiply(Int("x"), Integer(2)), Integer(10))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_StringPrefixSuffixAndLengthFactsCombine",
            [StartsWith(String("text"), Text("AB")), EndsWith(String("text"), Text("CD")), Equal(Length(String("text")), Integer(3))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_StringContainsAndConcatFactsCombine",
            [Equal(String("left"), Text("A")), Equal(String("right"), Text("B")), Not(Contains(Concat(String("left"), String("right")), Text("AB")))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_StringContainsLongerThanKnownLength_IsUnsatisfiable",
            [Equal(Length(String("text")), Integer(2)), Contains(String("text"), Text("ABC"))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_StringContainsWithExactLengthInfersValue",
            [Equal(Length(String("text")), Integer(2)), Contains(String("text"), Text("AB")), NotEqual(String("text"), Text("AB"))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_NegativeStringLengthBound_IsUnsatisfiable",
            [LessThan(Length(String("text")), Integer(0))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_DivideByConcreteZero_ReturnsUnknown",
            [Equal(Divide(Integer(10), Integer(0)), Integer(0))], Feasibility.Unknown);
        yield return CreateSolverCase("SmtSolver_DivideByZeroFromEquality_ReturnsUnknown",
            [Equal(Int("divisor"), Integer(0)), Equal(Divide(Integer(10), Int("divisor")), Integer(0))], Feasibility.Unknown);
        yield return CreateSolverCase("SmtSolver_DivisionWithNonZeroGuard_RemainsUsable",
            [Equal(Int("divisor"), Integer(2)), NotEqual(Divide(Integer(10), Int("divisor")), Integer(5))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_DivisionWithExplicitNonZeroGuard_RemainsUsable",
            [NotEqual(Int("divisor"), Integer(0)), Equal(Divide(Integer(10), Int("divisor")), Integer(5))], Feasibility.Satisfiable);
        yield return CreateSolverCase("SmtSolver_StrictBoundBeyondInt64Range_IsUnsatisfiableBeforeDivision",
            [GreaterThan(Int("divisor"), Integer(long.MaxValue)), Equal(Divide(Integer(10), Int("divisor")), Integer(0))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_RemainderWithRelationalNonZeroGuard_RemainsUsable",
            [GreaterThan(Int("divisor"), Integer(0)), Equal(Int("divisor"), Integer(2)), NotEqual(Remainder(Integer(5), Int("divisor")), Integer(1))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_NegativeDividendDivision_UsesCSharpTruncation",
            [GreaterThan(Int("dividend"), Integer(-2)), LessThan(Int("dividend"), Integer(0)), Equal(Divide(Int("dividend"), Integer(2)), Integer(0))], Feasibility.Satisfiable);
        yield return CreateSolverCase("SmtSolver_NegativeDividendRemainder_UsesCSharpSign",
            [GreaterThan(Int("dividend"), Integer(-2)), LessThan(Int("dividend"), Integer(0)), Equal(Remainder(Int("dividend"), Integer(2)), Integer(-1))], Feasibility.Satisfiable);
        yield return CreateSolverCase("SmtSolver_NegativeDivisorDivision_UsesCSharpTruncation",
            [GreaterThan(Int("divisor"), Integer(-3)), LessThan(Int("divisor"), Integer(0)), Equal(Divide(Integer(3), Int("divisor")), Integer(-1))], Feasibility.Satisfiable);
        yield return CreateSolverCase("SmtSolver_NonNegativeRemainderWithPositiveDivisor_ProvesRange",
            [GreaterThanOrEqual(Int("dividend"), Integer(0)), GreaterThan(Int("divisor"), Integer(0)), Or(LessThan(Remainder(Int("dividend"), Int("divisor")), Integer(0)), GreaterThanOrEqual(Remainder(Int("dividend"), Int("divisor")), Int("divisor")))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_SelectedConditionalSkipsUnsafeUnselectedIntegerBranch",
            [Bool("useSafeBranch"), NotEqual(Conditional(Bool("useSafeBranch"), Integer(7), Divide(Integer(1), Integer(0)), SmtValueKind.Int), Integer(7))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_NegatedComparisonGuardKeepsDivisionUsable",
            [Not(LessThanOrEqual(Int("divisor"), Integer(0))), NotEqual(Divide(Integer(10), Int("divisor")), Integer(999))], Feasibility.Satisfiable);
        yield return CreateSolverCase("SmtSolver_AffineRangeGuardKeepsDerivedDivisorUsable",
            [GreaterThan(Int("x"), Integer(1)), Equal(Divide(Integer(10), Subtract(Int("x"), Integer(1))), Integer(10))], Feasibility.Satisfiable);
        yield return CreateSolverCase("SmtSolver_AffineEqualityPropagatesSolvedValue",
            [Equal(Add(Int("x"), Integer(2)), Integer(5)), NotEqual(Int("x"), Integer(3))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_ConflictingStringPrefixesAreUnsatisfiable",
            [StartsWith(String("text"), Text("AB")), StartsWith(String("text"), Text("AC"))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_ExactLengthPrefixSuffixOverlapInfersString",
            [StartsWith(String("text"), Text("AB")), EndsWith(String("text"), Text("BC")), Equal(Length(String("text")), Integer(3)), NotEqual(String("text"), Text("ABC"))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_SelectedConditionalStringConcatSimplifiesPredicate",
            [Bool("usePrefix"), Not(StartsWith(Concat(Conditional(Bool("usePrefix"), Text("AB"), Text("CD"), SmtValueKind.String), Text("X")), Text("AB")))], Feasibility.Unsatisfiable);
        yield return CreateSolverCase("SmtSolver_BooleanAliasChainContradiction_IsPreprocessedWithoutZ3",
            [Equal(Bool("first"), Bool("second")), Bool("first"), Not(Bool("second"))], Feasibility.Unsatisfiable, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_IntegerAliasIntervalContradiction_IsPreprocessedWithoutZ3",
            [Equal(Int("alias"), Int("source")), GreaterThanOrEqual(Int("source"), Integer(5)), LessThan(Add(Int("alias"), Integer(1)), Integer(5))], Feasibility.Unsatisfiable, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_StringAliasPredicateContradiction_IsPreprocessedWithoutZ3",
            [Equal(String("copy"), String("text")), Equal(String("text"), Text("ABC")), Not(StartsWith(String("copy"), Text("A")))], Feasibility.Unsatisfiable, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_ReferenceAliasNullContradiction_IsPreprocessedWithoutZ3",
            [Equal(Reference("target"), Reference("source")), Equal(Reference("source"), Null()), NotEqual(Reference("target"), Null())], Feasibility.Unsatisfiable, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_DistinctNonNullReferencesRemainUnknownWithoutZ3",
            [NotEqual(Reference("left"), Null()), NotEqual(Reference("right"), Null()), NotEqual(Reference("left"), Reference("right"))], Feasibility.Unknown, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_IdenticalConditionalBranchesSimplifyWithoutZ3",
            [NotEqual(Conditional(Bool("flag"), Add(Int("value"), Integer(1)), Add(Int("value"), Integer(1)), SmtValueKind.Int), Add(Int("value"), Integer(1)))], Feasibility.Unsatisfiable, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_ConcatLengthNegativeComparison_IsPreprocessedWithoutZ3",
            [LessThan(Length(Concat(String("left"), String("right"))), Integer(0))], Feasibility.Unsatisfiable, zeroTimeout: true);
        yield return CreateSolverCase("SmtSolver_ReferenceNullAndNonNullConjunction_IsUnsatisfiable",
            [Equal(Reference("reference"), Null()), NotEqual(Reference("reference"), Null())], Feasibility.Unsatisfiable);
    }

    [TestCaseSource(nameof(SolverCases))]
    public void SolverMatrix(object value) {
        var testCase = (SolverCase)value;
        var timeout = testCase.ZeroTimeout ? TimeSpan.Zero : SolverTimeout;
        if (testCase.Conclusion == null) AssertSatisfiability(testCase.Expected, testCase.Conditions, timeout);
        else AssertImplication(testCase.Expected, testCase.Conditions, testCase.Conclusion, timeout);
    }

    private static TestCaseData CreateSolverCase(
        string name, SmtFormula[] conditions, Feasibility expected, SmtFormula? conclusion = null,
        bool zeroTimeout = false) =>
        new TestCaseData(new SolverCase(conditions, expected, conclusion, zeroTimeout)).SetName(name);
    [Test]
    public void AnalysisProof_NonNullGuard_MakesNullDereferenceProven() {
        using var search = new AnalysisProofSearch();
        var s = new SmtVariable("s", SmtValueKind.Reference);
        var sIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, s, new SmtNullConstant());
        var sIsNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, s, new SmtNullConstant());

        var result = search.Classify(
            new AnalysisProofQuery(
                new[] { sIsNotNull },
                new AnalysisHazard(AnalysisHazardKind.NullDereference, sIsNull)),
            TimeSpan.FromSeconds(2));

        Assert.That(result.Outcome, Is.EqualTo(AnalysisProofOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo("null_dereference_unreachable"));
    }
}
