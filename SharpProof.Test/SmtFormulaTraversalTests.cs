using System.Text.RegularExpressions;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
namespace SharpProof.Test;
[TestFixture]
public sealed class SmtFormulaTraversalTests {
    private static IEnumerable<TestCaseData> RebuildCases() {
        var oldBool = new SmtVariable("old_bool", SmtValueKind.Bool);
        var newBool = new SmtVariable("new_bool", SmtValueKind.Bool);
        var oldInt = new SmtVariable("old_int", SmtValueKind.Int);
        var newInt = new SmtVariable("new_int", SmtValueKind.Int);
        var oldString = new SmtVariable("old_string", SmtValueKind.String);
        var newString = new SmtVariable("new_string", SmtValueKind.String);
        var oldReference = new SmtVariable("old_reference", SmtValueKind.Reference);
        var newReference = new SmtVariable("new_reference", SmtValueKind.Reference);
        yield return Case("Unary",
            new SmtUnaryFormula(SmtUnaryOperator.Not, oldBool), oldBool, newBool,
            new SmtUnaryFormula(SmtUnaryOperator.Not, newBool));
        yield return Case("Binary",
            new SmtBinaryFormula(SmtBinaryOperator.Or, oldBool, new SmtBooleanConstant(false)), oldBool, newBool,
            new SmtBinaryFormula(SmtBinaryOperator.Or, newBool, new SmtBooleanConstant(false)));
        yield return Case("IntegerUnary",
            new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, oldInt), oldInt, newInt,
            new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, newInt));
        yield return Case("IntegerBinary",
            new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, oldInt, new SmtIntegerConstant(2)), oldInt, newInt,
            new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, newInt, new SmtIntegerConstant(2)));
        yield return Case("OpaqueIntegerBinary",
            new SmtOpaqueIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, oldInt, new SmtIntegerConstant(1)), oldInt, newInt,
            new SmtOpaqueIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, newInt, new SmtIntegerConstant(1)));
        yield return Case("StringLength",
            new SmtStringLengthTerm(oldString), oldString, newString, new SmtStringLengthTerm(newString));
        yield return Case("StringConcat",
            new SmtStringConcatTerm(oldString, new SmtStringConstant("tail")), oldString, newString,
            new SmtStringConcatTerm(newString, new SmtStringConstant("tail")));
        yield return Case("StringSubstring",
            new SmtStringSubstringTerm(oldString, new SmtIntegerConstant(1), new SmtIntegerConstant(2)), oldString, newString,
            new SmtStringSubstringTerm(newString, new SmtIntegerConstant(1), new SmtIntegerConstant(2)));
        yield return Case("StringContains",
            new SmtStringContainsFormula(oldString, new SmtStringConstant("x")), oldString, newString,
            new SmtStringContainsFormula(newString, new SmtStringConstant("x")));
        yield return Case("StringStartsWith",
            new SmtStringStartsWithFormula(oldString, new SmtStringConstant("x")), oldString, newString,
            new SmtStringStartsWithFormula(newString, new SmtStringConstant("x")));
        yield return Case("StringEndsWith",
            new SmtStringEndsWithFormula(oldString, new SmtStringConstant("x")), oldString, newString,
            new SmtStringEndsWithFormula(newString, new SmtStringConstant("x")));
        yield return Case("RegexMatch",
            new SmtRegexMatchFormula(oldString, "^x$", RegexOptions.CultureInvariant), oldString, newString,
            new SmtRegexMatchFormula(newString, "^x$", RegexOptions.CultureInvariant));
        yield return Case("RuntimeTypeTest",
            new SmtRuntimeTypeTestFormula(oldReference, "System.String"), oldReference, newReference,
            new SmtRuntimeTypeTestFormula(newReference, "System.String"));
        yield return Case("Conditional",
            new SmtConditionalFormula(oldBool, oldInt, new SmtIntegerConstant(0), SmtValueKind.Int), oldBool, newBool,
            new SmtConditionalFormula(newBool, oldInt, new SmtIntegerConstant(0), SmtValueKind.Int));
    }
    [TestCaseSource(nameof(RebuildCases))]
    public void RebuildPreservesEveryNodeShape(
        object rootValue,
        object targetValue,
        object replacementValue,
        object expectedValue) {
        var root = (SmtFormula)rootValue;
        var target = (SmtFormula)targetValue;
        var replacement = (SmtFormula)replacementValue;
        var rewritten = SmtFormulaTraversal.RewriteBottomUp(
            root,
            formula => ReferenceEquals(formula, target) ? replacement : formula,
            out var changed);
        Assert.Multiple(() => {
            Assert.That(changed, Is.True);
            Assert.That(rewritten, Is.EqualTo(expectedValue));
            Assert.That(rewritten, Is.Not.SameAs(root));
        });
        var identity = SmtFormulaTraversal.RewriteBottomUp(root, static formula => formula, out var identityChanged);
        Assert.Multiple(() => {
            Assert.That(identityChanged, Is.False);
            Assert.That(identity, Is.SameAs(root));
        });
    }
    private static TestCaseData Case(
        string name,
        SmtFormula root,
        SmtFormula target,
        SmtFormula replacement,
        SmtFormula expected) => new(root, target, replacement, expected) { TestName = name };
}
