using System.Text.RegularExpressions;
using NUnit.Framework;
using SharpProof.ProofCore.Analysis;
using SharpProof.ProofCore.Smt;

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
            RegexConstraint.StartsWith => [match, StartsWith(text, testCase.Value!)],
            RegexConstraint.LengthEqual => [match, Compare(SmtBinaryOperator.Equal, length, testCase.Length)],
            RegexConstraint.StartsWithAndLength =>
                [match, StartsWith(text, testCase.Value!), Compare(SmtBinaryOperator.Equal, length, testCase.Length)],
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

    private static SmtStringStartsWithFormula StartsWith(SmtFormula text, string prefix) =>
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

    [Test]
    public void SmtSolver_NonPositiveTimeout_ReturnsUnknown() {
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
    public void SmtSolver_MismatchedEqualitySorts_ReturnsUnknown() {
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
    public void SmtSolver_ConditionalIntegerTermHonorsSelectedBranch() {
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
    public void SmtSolver_AffineGuardImpliesExactValue_IsUnsatisfiable() {
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
    public void SmtSolver_MultiplicationByConstantContradictsRange() {
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
    public void SmtSolver_StringPrefixSuffixAndLengthFactsCombine() {
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
    public void SmtSolver_StringContainsAndConcatFactsCombine() {
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
    public void SmtSolver_StringContainsLongerThanKnownLength_IsUnsatisfiable() {
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
    public void SmtSolver_StringContainsWithExactLengthInfersValue() {
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
    public void SmtSolver_NegativeStringLengthBound_IsUnsatisfiable() {
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
    public void SmtSolver_DivideByConcreteZero_ReturnsUnknown() {
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
    public void SmtSolver_DivideByZeroFromEquality_ReturnsUnknown() {
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
    public void SmtSolver_DivisionWithNonZeroGuard_RemainsUsable() {
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
    public void SmtSolver_DivisionWithExplicitNonZeroGuard_RemainsUsable() {
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
    public void SmtSolver_StrictBoundBeyondInt64Range_IsUnsatisfiableBeforeDivision() {
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
    public void SmtSolver_RemainderWithRelationalNonZeroGuard_RemainsUsable() {
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
    public void SmtSolver_NegativeDividendDivision_UsesCSharpTruncation() {
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
    public void SmtSolver_NegativeDividendRemainder_UsesCSharpSign() {
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
    public void SmtSolver_NegativeDivisorDivision_UsesCSharpTruncation() {
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
    public void SmtSolver_NonNegativeRemainderWithPositiveDivisor_ProvesRange() {
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
    public void SmtSolver_SelectedConditionalSkipsUnsafeUnselectedIntegerBranch() {
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
    public void SmtSolver_NegatedComparisonGuardKeepsDivisionUsable() {
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
    public void SmtSolver_AffineRangeGuardKeepsDerivedDivisorUsable() {
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
    public void SmtSolver_AffineEqualityPropagatesSolvedValue() {
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
    public void SmtSolver_ConflictingStringPrefixesAreUnsatisfiable() {
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
    public void SmtSolver_ExactLengthPrefixSuffixOverlapInfersString() {
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
    public void SmtSolver_SelectedConditionalStringConcatSimplifiesPredicate() {
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
    public void SmtSolver_BooleanAliasChainContradiction_IsPreprocessedWithoutZ3() {
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
    public void SmtSolver_IntegerAliasIntervalContradiction_IsPreprocessedWithoutZ3() {
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
    public void SmtSolver_StringAliasPredicateContradiction_IsPreprocessedWithoutZ3() {
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
    public void SmtSolver_ReferenceAliasNullContradiction_IsPreprocessedWithoutZ3() {
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
    public void SmtSolver_DistinctNonNullReferencesRemainUnknownWithoutZ3() {
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
    public void SmtSolver_IdenticalConditionalBranchesSimplifyWithoutZ3() {
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
    public void SmtSolver_ConcatLengthNegativeComparison_IsPreprocessedWithoutZ3() {
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
    public void SmtSolver_ReferenceNullAndNonNullConjunction_IsUnsatisfiable() {
        var reference = new SmtVariable("reference", SmtValueKind.Reference);
        var isNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, reference, new SmtNullConstant());
        var isNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, reference, new SmtNullConstant());

        AssertSatisfiability(
            Feasibility.Unsatisfiable,
            new SmtFormula[] { isNull, isNotNull });
    }

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
