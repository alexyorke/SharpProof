using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicIrTests
{
    [Test]
    public void LowerCondition_EncodesIntegerRangeWithSameFormulaAsLegacyTranslator()
    {
        var context = CreateExpressionContext(
            "int x",
            "x > 0 && x < 10");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);

        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void LowerCondition_RepresentsStringLengthAsSharedLengthAtom()
    {
        var context = CreateExpressionContext(
            "string s, int n",
            "s.Length == n");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(fact.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(fact.Left, Is.TypeOf<SymbolicLengthTerm>());
        Assert.That(((SymbolicLengthTerm)fact.Left).Value, Is.TypeOf<SymbolicStringContentTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_IdentityPreservingAsValueMatchesLegacyTranslator()
    {
        var context = CreateExpressionContext(
            "string text",
            "(text as object) == text");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);

        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void LowerCondition_IdentityPreservingReferenceCastMatchesLegacyTranslator()
    {
        var context = CreateExpressionContext(
            "string text",
            "((object)text) == text");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);

        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void LowerCondition_StringLiteralEqualityEmitsNullSafeContentFacts()
    {
        var context = CreateExpressionContext(
            "string s",
            "s == \"A\"");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var conjunction = (SymbolicBinaryCondition)condition;

        Assert.That(conjunction.Operator, Is.EqualTo(SymbolicConditionOperator.And));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Left).Operator,
            Is.EqualTo(SymbolicRelationOperator.NotEqual));
        var equality = AssertFactCondition<SymbolicRelationAtom>(conjunction.Right);
        Assert.That(equality.Left, Is.TypeOf<SymbolicStringContentTerm>());
        Assert.That(equality.Right, Is.EqualTo(new SymbolicStringConstantTerm("A")));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_TupleEqualityEmitsElementFacts()
    {
        var context = CreateExpressionContext(
            "(int A, int B) left, (int A, int B) right",
            "left == right");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var conjunction = (SymbolicBinaryCondition)condition;

        Assert.That(conjunction.Operator, Is.EqualTo(SymbolicConditionOperator.And));
        var firstElement = (SymbolicVariableTerm)AssertFactCondition<SymbolicRelationAtom>(conjunction.Left).Left;
        var secondElement = (SymbolicVariableTerm)AssertFactCondition<SymbolicRelationAtom>(conjunction.Right).Left;
        Assert.That(firstElement.Name, Does.StartWith("left#").And.EndsWith(".Item1"));
        Assert.That(secondElement.Name, Does.StartWith("left#").And.EndsWith(".Item2"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_UncheckedEnumCastEqualityUsesIntegralEnumTerm()
    {
        var context = CreateExpressionContext(
            "Mode mode",
            "unchecked((int)mode) == 1",
            "public enum Mode { None = 0, Ready = 1 }");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var equality = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(equality.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        var mode = (SymbolicVariableTerm)equality.Left;
        Assert.That(mode.Name, Does.StartWith("mode#"));
        Assert.That(equality.Right, Is.EqualTo(new SymbolicIntegerConstantTerm(1)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_TypePatternUsesSharedTypeTestAtom()
    {
        var context = CreateExpressionContext(
            "object value",
            "value is string");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var conjunction = (SymbolicBinaryCondition)condition;

        Assert.That(conjunction.Operator, Is.EqualTo(SymbolicConditionOperator.And));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Left).Operator,
            Is.EqualTo(SymbolicRelationOperator.NotEqual));
        var typeTest = AssertFactCondition<SymbolicTypeTestAtom>(conjunction.Right);
        Assert.That(typeTest.TypeKey, Is.EqualTo("System.String"));
        Assert.That(typeTest.Value, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_DeclarationPatternUsesSharedTypeTestAtom()
    {
        var context = CreateExpressionContext(
            "object value",
            "value is string text");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var conjunction = (SymbolicBinaryCondition)condition;

        Assert.That(conjunction.Operator, Is.EqualTo(SymbolicConditionOperator.And));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Left).Right, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(AssertFactCondition<SymbolicTypeTestAtom>(conjunction.Right).TypeKey, Is.EqualTo("System.String"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_NegatedTypePatternNegatesSharedTypeTestFacts()
    {
        var context = CreateExpressionContext(
            "object value",
            "value is not string");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicNotCondition>());
        var negated = (SymbolicNotCondition)condition;
        Assert.That(negated.Operand, Is.TypeOf<SymbolicBinaryCondition>());
        var conjunction = (SymbolicBinaryCondition)negated.Operand;

        Assert.That(conjunction.Operator, Is.EqualTo(SymbolicConditionOperator.And));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Left).Right, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(AssertFactCondition<SymbolicTypeTestAtom>(conjunction.Right).TypeKey, Is.EqualTo("System.String"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_NullPatternUsesSharedNullRelation()
    {
        var context = CreateExpressionContext(
            "object value",
            "value is null");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_NegatedNullPatternUsesSharedNullRelation()
    {
        var context = CreateExpressionContext(
            "object value",
            "value is not null");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.NotEqual));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_IntegerConstantPatternUsesSharedRelation()
    {
        var context = CreateExpressionContext(
            "int value",
            "value is 42");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.EqualTo(new SymbolicIntegerConstantTerm(42)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);
        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void LowerCondition_NegatedIntegerConstantPatternUsesSharedRelation()
    {
        var context = CreateExpressionContext(
            "int value",
            "value is not 42");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.NotEqual));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.EqualTo(new SymbolicIntegerConstantTerm(42)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_BooleanConstantPatternUsesSharedRelation()
    {
        var context = CreateExpressionContext(
            "bool value",
            "value is true");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.EqualTo(new SymbolicBooleanConstantTerm(true)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_RelationalPatternUsesSharedRelation()
    {
        var context = CreateExpressionContext(
            "int value",
            "value is > 42");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.GreaterThan));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.EqualTo(new SymbolicIntegerConstantTerm(42)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);
        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void LowerCondition_NegatedRelationalPatternInvertsRelation()
    {
        var context = CreateExpressionContext(
            "int value",
            "value is not >= 42");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.LessThan));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.EqualTo(new SymbolicIntegerConstantTerm(42)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_EmptyRecursivePatternUsesSharedNullRelation()
    {
        var context = CreateExpressionContext(
            "object value",
            "value is { }");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.NotEqual));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);
        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void LowerCondition_NegatedEmptyRecursivePatternUsesSharedNullRelation()
    {
        var context = CreateExpressionContext(
            "object value",
            "value is not { }");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(relation.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(relation.Right, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_AndPatternComposesSharedRelations()
    {
        var context = CreateExpressionContext(
            "int value",
            "value is > 0 and < 10");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var conjunction = (SymbolicBinaryCondition)condition;

        Assert.That(conjunction.Operator, Is.EqualTo(SymbolicConditionOperator.And));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Left).Operator,
            Is.EqualTo(SymbolicRelationOperator.GreaterThan));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Right).Operator,
            Is.EqualTo(SymbolicRelationOperator.LessThan));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);
        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void LowerCondition_OrPatternComposesSharedRelations()
    {
        var context = CreateExpressionContext(
            "int value",
            "value is < 0 or > 10");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var disjunction = (SymbolicBinaryCondition)condition;

        Assert.That(disjunction.Operator, Is.EqualTo(SymbolicConditionOperator.Or));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(disjunction.Left).Operator,
            Is.EqualTo(SymbolicRelationOperator.LessThan));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(disjunction.Right).Operator,
            Is.EqualTo(SymbolicRelationOperator.GreaterThan));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_UnaryPatternNegatesComposedPattern()
    {
        var context = CreateExpressionContext(
            "int value",
            "value is not (> 0 and < 10)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicNotCondition>());
        var negated = (SymbolicNotCondition)condition;
        Assert.That(negated.Operand, Is.TypeOf<SymbolicBinaryCondition>());
        var conjunction = (SymbolicBinaryCondition)negated.Operand;

        Assert.That(conjunction.Operator, Is.EqualTo(SymbolicConditionOperator.And));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Left).Operator,
            Is.EqualTo(SymbolicRelationOperator.GreaterThan));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(conjunction.Right).Operator,
            Is.EqualTo(SymbolicRelationOperator.LessThan));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var irFormula), Is.True);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);
        Assert.That(irFormula, Is.EqualTo(legacyFormula));
    }

    [Test]
    public void KnownApiLowering_StringStartsWithEmitsDeclarativeStringPredicate()
    {
        var context = CreateExpressionContext(
            "string s",
            """s.StartsWith("A")""");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicStringPredicateAtom>(condition);

        Assert.That(fact.Predicate, Is.EqualTo(SymbolicStringPredicateKind.StartsWith));
        Assert.That(fact.Value, Is.TypeOf<SymbolicStringContentTerm>());
        Assert.That(fact.Argument, Is.EqualTo(new SymbolicStringConstantTerm("A")));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtStringStartsWithFormula>());
    }

    [TestCase("Contains")]
    [TestCase("StartsWith")]
    [TestCase("EndsWith")]
    public void KnownApiLowering_StringCharPredicateUsesDeclarativeStringPredicate(string methodName)
    {
        var context = CreateExpressionContext(
            "string s",
            $"s.{methodName}('A')");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicStringPredicateAtom>(condition);

        Assert.That(fact.Argument, Is.EqualTo(new SymbolicStringConstantTerm("A")));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [TestCase("Contains")]
    [TestCase("StartsWith")]
    [TestCase("EndsWith")]
    public void KnownApiLowering_StringOrdinalPredicateUsesDeclarativeStringPredicate(string methodName)
    {
        var context = CreateExpressionContext(
            "string s",
            $"""s.{methodName}("A", System.StringComparison.Ordinal)""");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicStringPredicateAtom>(condition);

        Assert.That(fact.Argument, Is.EqualTo(new SymbolicStringConstantTerm("A")));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_StringConcatPredicateUsesSharedConcatTerm()
    {
        var context = CreateExpressionContext(
            "string suffix",
            """("pre" + suffix).StartsWith("pre")""");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicStringPredicateAtom>(condition);

        Assert.That(fact.Value, Is.TypeOf<SymbolicStringConcatTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        var predicate = (SmtStringStartsWithFormula)formula;
        Assert.That(predicate.Value, Is.TypeOf<SmtStringConcatTerm>());
    }

    [Test]
    public void KnownApiLowering_InterpolatedStringPredicateUsesSharedConcatTerm()
    {
        var context = CreateExpressionContext(
            "string suffix",
            "$\"pre{suffix}\".StartsWith(\"pre\")");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicStringPredicateAtom>(condition);

        Assert.That(fact.Value, Is.TypeOf<SymbolicStringConcatTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtStringStartsWithFormula>());
    }

    [Test]
    public void KnownApiLowering_StaticStringEqualsUsesSharedEqualityFacts()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "string.Equals(left, right)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_StaticStringEqualsOrdinalUsesSharedEqualityFacts()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "string.Equals(left, right, System.StringComparison.Ordinal)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_InstanceStringEqualsUsesSharedEqualityFacts()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "left.Equals(right)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_InstanceStringEqualsOrdinalUsesSharedEqualityFacts()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "left.Equals(right, System.StringComparison.Ordinal)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_InstanceStringEqualsIgnoreCaseStaysOnLegacyPath()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "left.Equals(right, System.StringComparison.OrdinalIgnoreCase)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out _), Is.False);
    }

    [Test]
    public void KnownApiLowering_InstanceStringEqualsObjectFallsBackToLegacyTranslator()
    {
        var context = CreateExpressionContext(
            "string left, object right",
            "left.Equals(right)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out _), Is.False);
    }

    [Test]
    public void KnownApiLowering_StaticStringEqualsIgnoreCaseStaysOnLegacyPath()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "string.Equals(left, right, System.StringComparison.OrdinalIgnoreCase)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out _), Is.False);
    }

    [Test]
    public void KnownApiLowering_ObjectReferenceEqualsUsesReferenceEqualityAtom()
    {
        var context = CreateExpressionContext(
            "object? left, object? right",
            "object.ReferenceEquals(left, right)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(fact.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(fact.Left.Kind, Is.EqualTo(SmtValueKind.Reference));
        Assert.That(fact.Right.Kind, Is.EqualTo(SmtValueKind.Reference));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_RegexIsMatchEmitsDeclarativeRegexPredicate()
    {
        var context = CreateExpressionContext(
            "string s",
            """System.Text.RegularExpressions.Regex.IsMatch(s, @"\A[A-Z]+\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)""");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var fact = AssertFactCondition<SymbolicStringPredicateAtom>(condition);

        Assert.That(fact.Predicate, Is.EqualTo(SymbolicStringPredicateKind.RegexMatch));
        Assert.That(fact.Argument, Is.EqualTo(new SymbolicStringConstantTerm(@"\A[A-Z]+\z")));
        Assert.That(fact.RegexOptions, Is.EqualTo(RegexOptions.CultureInvariant));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtRegexMatchFormula>());
    }

    [Test]
    public void KnownApiLowering_RegexUnsupportedOptionsStayOnLegacyPath()
    {
        var context = CreateExpressionContext(
            "string s",
            """System.Text.RegularExpressions.Regex.IsMatch(s, "A", System.Text.RegularExpressions.RegexOptions.RightToLeft)""");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out _), Is.False);
    }

    [Test]
    public void KnownApiLowering_StringIsNullOrEmptyUsesNullnessAndLengthAtoms()
    {
        var context = CreateExpressionContext(
            "string s",
            "string.IsNullOrEmpty(s)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var disjunction = (SymbolicBinaryCondition)condition;

        Assert.That(disjunction.Operator, Is.EqualTo(SymbolicConditionOperator.Or));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(disjunction.Left).Right, Is.TypeOf<SymbolicNullTerm>());
        var emptyAtom = AssertFactCondition<SymbolicRelationAtom>(disjunction.Right);
        Assert.That(emptyAtom.Left, Is.TypeOf<SymbolicLengthTerm>());
        Assert.That(emptyAtom.Right, Is.EqualTo(new SymbolicIntegerConstantTerm(0)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_StringIsNullOrWhiteSpaceUsesNullnessAndRegexAtoms()
    {
        var context = CreateExpressionContext(
            "string s",
            "string.IsNullOrWhiteSpace(s)");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var disjunction = (SymbolicBinaryCondition)condition;

        Assert.That(disjunction.Operator, Is.EqualTo(SymbolicConditionOperator.Or));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(disjunction.Left).Right, Is.TypeOf<SymbolicNullTerm>());
        var regexAtom = AssertFactCondition<SymbolicStringPredicateAtom>(disjunction.Right);
        Assert.That(regexAtom.Predicate, Is.EqualTo(SymbolicStringPredicateKind.RegexMatch));
        Assert.That(regexAtom.Argument, Is.EqualTo(new SymbolicStringConstantTerm(@"\A\s*\z")));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void KnownApiLowering_StringIsNullOrEmptyOverConcatOmitsImpossibleNullBranch()
    {
        var context = CreateExpressionContext(
            "string s",
            """string.IsNullOrEmpty("A" + s)""");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var atom = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(atom.Left, Is.TypeOf<SymbolicLengthTerm>());
        Assert.That(((SymbolicLengthTerm)atom.Left).Value, Is.TypeOf<SymbolicStringConcatTerm>());
    }

    [Test]
    public void LowerTerm_InstanceReferencePropertyUsesSharedMemberTerm()
    {
        var context = CreateExpressionContext(
            "Holder holder",
            "holder.Value",
            "public sealed class Holder { public string? Value { get; set; } }");

        Assert.That(SymbolicIrLowerer.TryLowerTerm(context.Expression, context.LoweringContext, out var term), Is.True);
        var member = (SymbolicMemberTerm)term;

        Assert.That(member.Receiver, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(member.MemberName, Is.EqualTo("Value"));
        Assert.That(member.Kind, Is.EqualTo(SmtValueKind.Reference));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(member, out var formula), Is.True);
        Assert.That(formula,
            Is.EqualTo(new SmtVariable(((SymbolicVariableTerm)member.Receiver).Name + ".Value",
                SmtValueKind.Reference)));
    }

    [Test]
    public void LowerTerm_InstanceIntegerPropertyUsesSharedMemberTerm()
    {
        var context = CreateExpressionContext(
            "Holder holder",
            "holder.Number",
            "public sealed class Holder { public int Number { get; set; } }");

        Assert.That(SymbolicIrLowerer.TryLowerTerm(context.Expression, context.LoweringContext, out var term), Is.True);
        var member = (SymbolicMemberTerm)term;

        Assert.That(member.Receiver, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(member.MemberName, Is.EqualTo("Number"));
        Assert.That(member.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(member, out var formula), Is.True);
        Assert.That(formula,
            Is.EqualTo(new SmtVariable(((SymbolicVariableTerm)member.Receiver).Name + ".Number", SmtValueKind.Int)));
    }

    [Test]
    public void LowerTerm_ImplicitThisStringPropertyUsesSharedMemberReference()
    {
        const string source = """
                              public sealed class C
                              {
                                  public string Text { get; set; }

                                  public string M()
                                  {
                                      return Text;
                                  }
                              }
                              """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SymbolicIrImplicitThisStringMember",
            new[] { syntaxTree },
            AnalyzerTestHost.GetMinimalFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var expression = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Single(identifier => identifier.Identifier.ValueText == "Text");
        var loweringContext = new SymbolicLoweringContext(semanticModel, CancellationToken.None);

        Assert.That(SymbolicIrLowerer.TryLowerTerm(expression, loweringContext, out var term), Is.True);
        var member = (SymbolicMemberTerm)term;

        Assert.That(member.Receiver, Is.EqualTo(new SymbolicVariableTerm("this", SmtValueKind.Reference)));
        Assert.That(member.MemberName, Is.EqualTo("Text"));
        Assert.That(member.Kind, Is.EqualTo(SmtValueKind.Reference));
        Assert.That(SymbolicIrLowerer.TryLowerStringTerm(expression, loweringContext, out var stringTerm), Is.True);
        Assert.That(stringTerm, Is.TypeOf<SymbolicStringContentTerm>());
    }

    [Test]
    public void LowerTerm_ConditionalAccessStringPropertyUsesConditionalReference()
    {
        const string source = """
                              public sealed class Holder
                              {
                                  public string Text { get; set; }
                              }

                              public sealed class C
                              {
                                  public string M(Holder holder)
                                  {
                                      return holder?.Text;
                                  }
                              }
                              """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SymbolicIrConditionalAccessStringMember",
            new[] { syntaxTree },
            AnalyzerTestHost.GetMinimalFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var expression = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Single();
        var loweringContext = new SymbolicLoweringContext(semanticModel, CancellationToken.None);

        Assert.That(SymbolicIrLowerer.TryLowerTerm(expression, loweringContext, out var term), Is.True);
        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;

        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicMemberTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(SymbolicIrLowerer.TryLowerStringTerm(expression, loweringContext, out var stringTerm), Is.True);
        Assert.That(stringTerm, Is.TypeOf<SymbolicStringContentTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(stringTerm, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
        Assert.That(((SmtConditionalFormula)formula).WhenTrue.Kind, Is.EqualTo(SmtValueKind.String));
        Assert.That(((SmtConditionalFormula)formula).WhenFalse.Kind, Is.EqualTo(SmtValueKind.String));
    }

    [Test]
    public void LowerTerm_ArrayElementUsesSharedElementTerm()
    {
        var context = CreateExpressionContext(
            "int[] values, int index",
            "values[index]");

        Assert.That(SymbolicIrLowerer.TryLowerTerm(context.Expression, context.LoweringContext, out var term), Is.True);
        var element = (SymbolicElementTerm)term;

        Assert.That(element.Receiver, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(element.Index, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(element.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(element, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
        Assert.That(
            ((SmtVariable)formula).Name,
            Does.StartWith(((SymbolicVariableTerm)element.Receiver).Name + "["));
    }

    [Test]
    public void KnownApiLowering_StringComparisonOverloadFallsBackToLegacyTranslator()
    {
        var context = CreateExpressionContext(
            "string s",
            """s.StartsWith("A", System.StringComparison.OrdinalIgnoreCase)""");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out _), Is.False);
        Assert.That(
            CSharpConditionToFormula.TryTranslate(context.Expression, context.SemanticModel, CancellationToken.None,
                out var legacyFormula), Is.True);
        Assert.That(legacyFormula, Is.Not.Null);
    }

    [Test]
    public void LowerCondition_DivisionAndRemainderUseSharedBinaryTerms()
    {
        var context = CreateExpressionContext(
            "int value, int divisor",
            "value / divisor == value % divisor");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(relation.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(relation.Left, Is.TypeOf<SymbolicBinaryTerm>());
        Assert.That(relation.Right, Is.TypeOf<SymbolicBinaryTerm>());
        Assert.That(((SymbolicBinaryTerm)relation.Left).Operator, Is.EqualTo(SymbolicBinaryTermOperator.Divide));
        Assert.That(((SymbolicBinaryTerm)relation.Right).Operator, Is.EqualTo(SymbolicBinaryTermOperator.Remainder));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_UnaryMinusUsesSharedBinaryTerm()
    {
        var context = CreateExpressionContext(
            "int value",
            "-value == 0");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);
        var negation = (SymbolicBinaryTerm)relation.Left;

        Assert.That(negation.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Subtract));
        Assert.That(negation.Left, Is.EqualTo(new SymbolicIntegerConstantTerm(0)));
        Assert.That(negation.Right, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_ConditionalExpressionUsesSharedConditionalTerm()
    {
        var context = CreateExpressionContext(
            "bool flag, int left, int right",
            "(flag ? left : right) == 0");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);
        var conditional = (SymbolicConditionalTerm)relation.Left;

        Assert.That(conditional.Condition, Is.TypeOf<SymbolicFactCondition>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(conditional.WhenTrue.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_ReferenceCoalesceUsesSharedConditionalTerm()
    {
        var context = CreateExpressionContext(
            "object? left, object? right",
            "(left ?? right) == null");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var relation = AssertFactCondition<SymbolicRelationAtom>(condition);
        var conditional = (SymbolicConditionalTerm)relation.Left;

        Assert.That(conditional.Condition, Is.TypeOf<SymbolicFactCondition>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(conditional.WhenTrue.Kind, Is.EqualTo(SmtValueKind.Reference));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_BigIntegerZeroOneUseIntegralAtoms()
    {
        var context = CreateExpressionContext(
            "System.Numerics.BigInteger value",
            "value >= System.Numerics.BigInteger.MinusOne && value > System.Numerics.BigInteger.Zero && value <= System.Numerics.BigInteger.One");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_StringEmptyUsesStaticStringConstantTerm()
    {
        var source = """
                     public sealed class C
                     {
                         public void M()
                         {
                             _ = string.Empty;
                         }
                     }
                     """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SymbolicIrStringEmptyTest",
            new[] { syntaxTree },
            AnalyzerTestHost.GetMinimalFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var expression = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Single(static memberAccess => memberAccess.ToString() == "string.Empty");
        var loweringContext = new SymbolicLoweringContext(semanticModel, CancellationToken.None);

        Assert.That(SymbolicIrLowerer.TryLowerTerm(expression, loweringContext, out var term), Is.True);

        Assert.That(term, Is.EqualTo(new SymbolicStringConstantTerm(string.Empty)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtStringConstant(string.Empty)));
    }

    [Test]
    public void LowerCondition_StringNonNullHelper_TreatsConcatAsAlwaysNonNull()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "left + right");

        Assert.That(
            SymbolicIrLowerer.TryLowerStringNonNullCondition(
                context.Expression,
                context.LoweringContext,
                out var condition),
            Is.True);

        Assert.That(condition, Is.EqualTo(new SymbolicConstantCondition(true)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtBooleanConstant(true)));
    }

    [Test]
    public void LowerCondition_StringNonNullHelper_LowersCoalesceToOperandDisjunction()
    {
        var context = CreateExpressionContext(
            "string left, string right",
            "left ?? right");

        Assert.That(
            SymbolicIrLowerer.TryLowerStringNonNullCondition(
                context.Expression,
                context.LoweringContext,
                out var condition),
            Is.True);

        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var disjunction = (SymbolicBinaryCondition)condition;
        Assert.That(disjunction.Operator, Is.EqualTo(SymbolicConditionOperator.Or));
        Assert.That(disjunction.Left, Is.TypeOf<SymbolicFactCondition>());
        Assert.That(disjunction.Right, Is.TypeOf<SymbolicFactCondition>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_StringNonNullHelper_LowersConditionalToBranchSensitiveCondition()
    {
        var context = CreateExpressionContext(
            "bool flag, string first, string second",
            "flag ? first : second");

        Assert.That(
            SymbolicIrLowerer.TryLowerStringNonNullCondition(
                context.Expression,
                context.LoweringContext,
                out var condition),
            Is.True);

        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var outerOr = (SymbolicBinaryCondition)condition;
        Assert.That(outerOr.Operator, Is.EqualTo(SymbolicConditionOperator.Or));
        Assert.That(outerOr.Left, Is.TypeOf<SymbolicBinaryCondition>());
        Assert.That(outerOr.Right, Is.TypeOf<SymbolicBinaryCondition>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerCondition_NullableHasValueUsesSharedNullableTerm()
    {
        var context = CreateExpressionContext(
            "int? maybe",
            "maybe.HasValue");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var atom = AssertFactCondition<SymbolicTruthAtom>(condition);

        Assert.That(atom.Condition, Is.TypeOf<SymbolicNullableHasValueTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
        var variable = (SmtVariable)formula;
        Assert.That(variable.Kind, Is.EqualTo(SmtValueKind.Bool));
        Assert.That(variable.Name, Does.StartWith("maybe#"));
        Assert.That(variable.Name, Does.EndWith(".HasValue"));
    }

    [Test]
    public void LowerCondition_NullableValueUsesSharedNullableValueTerm()
    {
        var context = CreateExpressionContext(
            "int? maybe, int expected",
            "maybe.Value == expected");

        Assert.That(SymbolicIrLowerer.TryLowerCondition(context.Expression, context.LoweringContext, out var condition),
            Is.True);
        var atom = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(atom.Operator, Is.EqualTo(SymbolicRelationOperator.Equal));
        Assert.That(atom.Left, Is.TypeOf<SymbolicNullableValueTerm>());
        var nullableValue = (SymbolicNullableValueTerm)atom.Left;
        Assert.That(nullableValue.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(nullableValue.NullableName, Does.StartWith("maybe#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
        Assert.That(formula, Is.TypeOf<SmtBinaryFormula>());
        var binary = (SmtBinaryFormula)formula;
        Assert.That(binary.Left, Is.TypeOf<SmtVariable>());
        Assert.That(((SmtVariable)binary.Left).Name, Does.EndWith(".Value"));
    }

    [Test]
    public void LowerTerm_NullableGetValueOrDefaultUsesConditionalDefaultTerm()
    {
        var context = CreateExpressionContext(
            "int? maybe",
            "maybe.GetValueOrDefault() == 0");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;
        Assert.That(AssertFactCondition<SymbolicTruthAtom>(conditional.Condition).Condition,
            Is.TypeOf<SymbolicNullableHasValueTerm>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicNullableValueTerm>());
        Assert.That(conditional.WhenFalse, Is.EqualTo(new SymbolicIntegerConstantTerm(0)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(conditional, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
    }

    [Test]
    public void LowerTerm_NullableGetValueOrDefaultFallbackUsesConditionalFallbackTerm()
    {
        var context = CreateExpressionContext(
            "int? maybe, int fallback",
            "maybe.GetValueOrDefault(fallback) == fallback");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;
        Assert.That(AssertFactCondition<SymbolicTruthAtom>(conditional.Condition).Condition,
            Is.TypeOf<SymbolicNullableHasValueTerm>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicNullableValueTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)conditional.WhenFalse).Name, Does.StartWith("fallback#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(conditional, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
    }

    [Test]
    public void LowerTerm_NullableBoolGetValueOrDefaultUsesConditionalDefaultTerm()
    {
        var context = CreateExpressionContext(
            "bool? maybe",
            "maybe.GetValueOrDefault() == false");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;
        Assert.That(AssertFactCondition<SymbolicTruthAtom>(conditional.Condition).Condition,
            Is.TypeOf<SymbolicNullableHasValueTerm>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicNullableValueTerm>());
        Assert.That(conditional.WhenTrue.Kind, Is.EqualTo(SmtValueKind.Bool));
        Assert.That(conditional.WhenFalse, Is.EqualTo(new SymbolicBooleanConstantTerm(false)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(conditional, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void LowerTerm_NullableCoalesceUnderlyingFallbackUsesConditionalValueTerm()
    {
        var context = CreateExpressionContext(
            "int? maybe, int fallback",
            "(maybe ?? fallback) == fallback");
        var coalesce = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(coalesce, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;
        Assert.That(AssertFactCondition<SymbolicTruthAtom>(conditional.Condition).Condition,
            Is.TypeOf<SymbolicNullableHasValueTerm>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicNullableValueTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)conditional.WhenFalse).Name, Does.StartWith("fallback#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(conditional, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
    }

    [Test]
    public void LowerTerm_NullableConditionalAccessCoalesceUsesElementValueTerm()
    {
        var context = CreateExpressionContext(
            "int[] values, int fallback",
            "(values?[0] ?? fallback) == fallback");
        var coalesce = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(coalesce, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;
        var hasValue = AssertFactCondition<SymbolicTruthAtom>(conditional.Condition);
        Assert.That(hasValue.Condition, Is.TypeOf<SymbolicConditionalTerm>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicElementTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)conditional.WhenFalse).Name, Does.StartWith("fallback#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(conditional, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
    }

    [Test]
    public void LowerTerm_NullableConditionalAccessMultidimensionalArrayLengthUsesDimensionProduct()
    {
        var context = CreateExpressionContext(
            "int[,] values, int fallback",
            "(values?.Length ?? fallback) == fallback");
        var coalesce = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(coalesce, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;
        var hasValue = AssertFactCondition<SymbolicTruthAtom>(conditional.Condition);
        Assert.That(hasValue.Condition, Is.TypeOf<SymbolicConditionalTerm>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicBinaryTerm>());
        var multiply = (SymbolicBinaryTerm)conditional.WhenTrue;
        Assert.That(multiply.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Multiply));
        Assert.That(multiply.Left, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        Assert.That(multiply.Right, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)conditional.WhenFalse).Name, Does.StartWith("fallback#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(conditional, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
    }

    [Test]
    public void LowerTerm_NullableEnumGetValueOrDefaultUsesIntegralDefaultTerm()
    {
        var context = CreateExpressionContext(
            "Status? maybe",
            "maybe.GetValueOrDefault() == Status.None",
            "public enum Status { None = 0, Active = 1 }");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicConditionalTerm>());
        var conditional = (SymbolicConditionalTerm)term;
        Assert.That(AssertFactCondition<SymbolicTruthAtom>(conditional.Condition).Condition,
            Is.TypeOf<SymbolicNullableHasValueTerm>());
        Assert.That(conditional.WhenTrue, Is.TypeOf<SymbolicNullableValueTerm>());
        Assert.That(conditional.WhenTrue.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(conditional.WhenFalse, Is.EqualTo(new SymbolicIntegerConstantTerm(0)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(conditional, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtConditionalFormula>());
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Int));
    }

    [Test]
    public void Encoder_ExceptionPreconditionUsesTriggerFormulaWithoutSpecialAnalyzerRule()
    {
        var divisor = new SymbolicVariableTerm("d#1", SmtValueKind.Int);
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                divisor,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("d == 0"),
            "test.divide-by-zero"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                divisor,
                trigger),
            SyntaxFactory.ParseExpression("1 / d"),
            "test.exception-precondition"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtVariable("d#1", SmtValueKind.Int),
            new SmtIntegerConstant(0))));
    }

    [Test]
    public void Encoder_BoundsExceptionPreconditionUsesSharedBoundsAtom()
    {
        var index = new SymbolicVariableTerm("i#1", SmtValueKind.Int);
        var length = new SymbolicVariableTerm("values#1.Length", SmtValueKind.Int);
        var inRange = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                index,
                length,
                true,
                true),
            SyntaxFactory.ParseExpression("values[i]"),
            "test.bounds"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.IndexOutOfRange,
                index,
                new SymbolicNotCondition(inRange)),
            SyntaxFactory.ParseExpression("values[i]"),
            "test.exception-precondition.bounds"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtUnaryFormula>());
        var negated = (SmtUnaryFormula)formula;
        Assert.That(negated.Operator, Is.EqualTo(SmtUnaryOperator.Not));
        Assert.That(negated.Operand, Is.TypeOf<SmtBinaryFormula>());
    }

    [Test]
    public void Encoder_NegativeLengthExceptionPreconditionUsesRelationAtom()
    {
        var length = new SymbolicVariableTerm("length#1", SmtValueKind.Int);
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                length,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("new int[length]"),
            "test.negative-length"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.NegativeLength,
                length,
                trigger),
            SyntaxFactory.ParseExpression("new int[length]"),
            "test.exception-precondition.negative-length"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtBinaryFormula(
            SmtBinaryOperator.LessThan,
            new SmtVariable("length#1", SmtValueKind.Int),
            new SmtIntegerConstant(0))));
    }

    [Test]
    public void Encoder_CheckedOverflowExceptionPreconditionUsesOutOfRangeAtoms()
    {
        var result = new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Add,
            new SymbolicVariableTerm("left#1", SmtValueKind.Int),
            new SymbolicVariableTerm("right#1", SmtValueKind.Int));
        var lowerOverflow = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                result,
                new SymbolicIntegerConstantTerm(int.MinValue)),
            SyntaxFactory.ParseExpression("left + right"),
            "test.checked-overflow.below-min"));
        var upperOverflow = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                result,
                new SymbolicIntegerConstantTerm(int.MaxValue)),
            SyntaxFactory.ParseExpression("left + right"),
            "test.checked-overflow.above-max"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                result,
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    lowerOverflow,
                    upperOverflow)),
            SyntaxFactory.ParseExpression("checked(left + right)"),
            "test.exception-precondition.checked-overflow"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtBinaryFormula>());
        var disjunction = (SmtBinaryFormula)formula;
        Assert.That(disjunction.Operator, Is.EqualTo(SmtBinaryOperator.Or));
        Assert.That(disjunction.Left, Is.TypeOf<SmtBinaryFormula>());
        Assert.That(disjunction.Right, Is.TypeOf<SmtBinaryFormula>());
    }

    [Test]
    public void Encoder_NullDereferenceExceptionPreconditionUsesNullnessAtom()
    {
        var receiver = new SymbolicVariableTerm("text#1", SmtValueKind.Reference);
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                receiver,
                new SymbolicNullTerm()),
            SyntaxFactory.ParseExpression("text.Length"),
            "test.null-dereference"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.NullDereference,
                receiver,
                trigger),
            SyntaxFactory.ParseExpression("text.Length"),
            "test.exception-precondition.null-dereference"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtVariable("text#1", SmtValueKind.Reference),
            new SmtNullConstant())));
    }

    [Test]
    public void Encoder_UnboxNullExceptionPreconditionUsesNullnessAtom()
    {
        var value = new SymbolicVariableTerm("value#1", SmtValueKind.Reference);
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                value,
                new SymbolicNullTerm()),
            SyntaxFactory.ParseExpression("(int)value"),
            "test.unbox-null"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.UnboxNull,
                value,
                trigger),
            SyntaxFactory.ParseExpression("(int)value"),
            "test.exception-precondition.unbox-null"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtVariable("value#1", SmtValueKind.Reference),
            new SmtNullConstant())));
    }

    [Test]
    public void Encoder_ArgumentNullExceptionPreconditionUsesNullnessAtom()
    {
        var argument = new SymbolicVariableTerm("gate#1", SmtValueKind.Reference);
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                argument,
                new SymbolicNullTerm()),
            SyntaxFactory.ParseExpression("lock (gate) { }"),
            "test.argument-null"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.ArgumentNull,
                argument,
                trigger),
            SyntaxFactory.ParseExpression("lock (gate) { }"),
            "test.exception-precondition.argument-null"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            new SmtVariable("gate#1", SmtValueKind.Reference),
            new SmtNullConstant())));
    }

    [Test]
    public void Encoder_NullableValueExceptionPreconditionUsesHasValueAtom()
    {
        var hasValue = new SymbolicNullableHasValueTerm("maybe#1");
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicTruthAtom(hasValue),
            SyntaxFactory.ParseExpression("maybe.Value"),
            "test.nullable-value"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
                new SymbolicVariableTerm("maybe#1", SmtValueKind.Reference),
                new SymbolicNotCondition(trigger)),
            SyntaxFactory.ParseExpression("maybe.Value"),
            "test.exception-precondition.nullable-value"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtVariable("maybe#1.HasValue", SmtValueKind.Bool))));
    }

    [Test]
    public void Encoder_InvalidCastExceptionPreconditionUsesTypeTestAtom()
    {
        var value = new SymbolicVariableTerm("value#1", SmtValueKind.Reference);
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicTypeTestAtom(value, "System.String"),
            SyntaxFactory.ParseExpression("(string)value"),
            "test.invalid-cast"));
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.InvalidCast,
                value,
                new SymbolicNotCondition(trigger)),
            SyntaxFactory.ParseExpression("(string)value"),
            "test.exception-precondition.invalid-cast"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtUnaryFormula(
            SmtUnaryOperator.Not,
            new SmtRuntimeTypeTestFormula(
                new SmtVariable("value#1", SmtValueKind.Reference),
                "System.String"))));
    }

    [Test]
    public void Encoder_DirectThrowExceptionPreconditionUsesConstantTrigger()
    {
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DirectThrow,
                null,
                new SymbolicConstantCondition(true)),
            SyntaxFactory.ParseStatement("throw new System.Exception();"),
            "test.exception-precondition.direct-throw"));

        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtBooleanConstant(true)));
    }

    [Test]
    public void Encoder_OwnershipResourceAtomsStayConservativeUntilProofSemanticsExist()
    {
        var owner = new SymbolicVariableTerm("owner#1", SmtValueKind.Reference);
        var alias = new SymbolicVariableTerm("alias#1", SmtValueKind.Reference);
        var resource = new SymbolicVariableTerm("resource#1", SmtValueKind.Reference);
        var source = SyntaxFactory.ParseExpression("resource");
        var atoms = new SymbolicAtom[]
        {
            new SymbolicFreshnessAtom(owner),
            new SymbolicOwnershipAtom(owner, false),
            new SymbolicAliasAtom(owner, alias, true),
            new SymbolicBorrowAtom(owner, alias, SymbolicBorrowKind.Shared),
            new SymbolicEscapeAtom(owner, SymbolicEscapeKind.Return),
            new SymbolicReturnedOwnershipAtom(owner),
            new SymbolicMutationAtom(owner, false),
            new SymbolicDisposalAtom(resource, SymbolicDisposalState.Disposed),
            new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Owned)
        };

        foreach (var atom in atoms)
        {
            var condition = new SymbolicFactCondition(SymbolicFact.Exact(atom, source, "test.ownership-resource"));

            Assert.That(
                SymbolicIrFormulaEncoder.TryEncode(condition, out _),
                Is.False,
                atom.GetType().Name + " should not be encoded optimistically.");
        }
    }

    [Test]
    public void SymbolicFactInfo_ProjectsOwnershipResourceFactsWithoutSolverTypes()
    {
        var resource = new SymbolicVariableTerm("resource#1", SmtValueKind.Reference);
        var fact = SymbolicFact.Exact(
            new SymbolicDisposalAtom(resource, SymbolicDisposalState.Disposed),
            SyntaxFactory.ParseExpression("resource.Dispose()"),
            "test.disposal",
            evidenceKey: "evidence.resource.disposed");

        var info = SymbolicFactInfo.FromFact(fact);

        Assert.That(info.Kind, Is.EqualTo(nameof(SymbolicDisposalAtom)));
        Assert.That(info.Text, Does.Contain(nameof(SymbolicDisposalState.Disposed)));
        Assert.That(info.Text, Does.Not.Contain(nameof(SmtFormula)));
        Assert.That(info.Provenance, Is.EqualTo("test.disposal"));
        Assert.That(info.EvidenceKey, Is.EqualTo("evidence.resource.disposed"));
    }

    [Test]
    public void OwnershipFactFactory_CreatesConsistentFreshOwnedResourceFacts()
    {
        var value = new SymbolicVariableTerm("value#1", SmtValueKind.Reference);
        var syntax = SyntaxFactory.ParseExpression("new MutableResource()");

        var facts = SymbolicOwnershipFactFactory.CreateFreshOwned(
            value,
            syntax,
            "test.ownership",
            evidenceKey: "evidence.ownership");

        Assert.That(facts, Has.Length.EqualTo(3));
        Assert.That(facts[0].Atom, Is.EqualTo(new SymbolicFreshnessAtom(value)));
        Assert.That(facts[1].Atom, Is.EqualTo(new SymbolicOwnershipAtom(value, false)));
        Assert.That(facts[2].Atom,
            Is.EqualTo(new SymbolicResourceLifetimeAtom(value, SymbolicResourceLifetimeState.Owned)));
        Assert.That(facts.Select(static fact => fact.Provenance), Is.EqualTo(new[]
        {
            "test.ownership.fresh",
            "test.ownership.owned",
            "test.ownership.lifetime"
        }));
        Assert.That(facts.All(static fact => fact.Confidence == SymbolicFactConfidence.Exact), Is.True);
        Assert.That(facts.All(static fact => fact.EvidenceKey == "evidence.ownership"), Is.True);
    }

    [Test]
    public void OwnershipFactFactory_CreatesFreshOwnedValueFactsWithoutResourceLifetime()
    {
        var value = new SymbolicVariableTerm("array#1", SmtValueKind.Reference);
        var syntax = SyntaxFactory.ParseExpression("new int[1]");

        var facts = SymbolicOwnershipFactFactory.CreateFreshOwnedValue(
            value,
            syntax,
            "test.array",
            evidenceKey: "evidence.array");

        Assert.That(facts, Has.Length.EqualTo(2));
        Assert.That(facts[0].Atom, Is.EqualTo(new SymbolicFreshnessAtom(value)));
        Assert.That(facts[1].Atom, Is.EqualTo(new SymbolicOwnershipAtom(value, false)));
        Assert.That(facts.Any(static fact => fact.Atom is SymbolicResourceLifetimeAtom), Is.False);
        Assert.That(facts.Select(static fact => fact.Provenance), Is.EqualTo(new[]
        {
            "test.array.fresh",
            "test.array.owned"
        }));
        Assert.That(facts.All(static fact => fact.Confidence == SymbolicFactConfidence.Exact), Is.True);
        Assert.That(facts.All(static fact => fact.EvidenceKey == "evidence.array"), Is.True);
    }

    [Test]
    public void OwnershipFactFactory_CreatesAliasBorrowEscapeMutationAndDisposalFacts()
    {
        var owner = new SymbolicVariableTerm("owner#1", SmtValueKind.Reference);
        var alias = new SymbolicVariableTerm("alias#1", SmtValueKind.Reference);
        var syntax = SyntaxFactory.ParseExpression("owner");
        var facts = new[]
        {
            SymbolicOwnershipFactFactory.CreateAlias(owner, alias, true, syntax, "test.alias"),
            SymbolicOwnershipFactFactory.CreateBorrow(owner, alias, SymbolicBorrowKind.Mutable, syntax, "test.borrow"),
            SymbolicOwnershipFactFactory.CreateEscape(owner, SymbolicEscapeKind.Argument, syntax, "test.escape"),
            SymbolicOwnershipFactFactory.CreateReturnedOwnership(owner, syntax, "test.returned"),
            SymbolicOwnershipFactFactory.CreateMutation(owner, true, syntax, "test.mutation"),
            SymbolicOwnershipFactFactory.CreateDisposal(owner, SymbolicDisposalState.MaybeDisposed, syntax,
                "test.disposal"),
            SymbolicOwnershipFactFactory.CreateResourceLifetime(owner, SymbolicResourceLifetimeState.Escaped, syntax,
                "test.lifetime")
        };
        var state = new SymbolicState(facts);
        var infos = SymbolicFactInfo.FromState(state);

        Assert.That(infos.Select(static info => info.Kind), Is.EqualTo(new[]
        {
            nameof(SymbolicAliasAtom),
            nameof(SymbolicBorrowAtom),
            nameof(SymbolicEscapeAtom),
            nameof(SymbolicReturnedOwnershipAtom),
            nameof(SymbolicMutationAtom),
            nameof(SymbolicDisposalAtom),
            nameof(SymbolicResourceLifetimeAtom)
        }));
        Assert.That(infos.Select(static info => info.Provenance), Is.EqualTo(new[]
        {
            "test.alias",
            "test.borrow",
            "test.escape",
            "test.returned",
            "test.mutation",
            "test.disposal",
            "test.lifetime"
        }));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(facts[5], out _), Is.False);
    }

    [Test]
    public void SmtFormulaLowerer_LengthLowerBoundUsesSharedLengthAtom()
    {
        var sourceNode = SyntaxFactory.ParseExpression("items");
        var smtFormula = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            new SmtVariable("items#1.Length", SmtValueKind.Int),
            new SmtIntegerConstant(2));

        Assert.That(SymbolicSmtFormulaLowerer.TryLowerCondition(
            smtFormula,
            sourceNode,
            "test.smt-lowerer.length",
            "test.smt-lowerer.length",
            out var condition), Is.True);
        var atom = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(atom.Operator, Is.EqualTo(SymbolicRelationOperator.GreaterThanOrEqual));
        Assert.That(atom.Left, Is.TypeOf<SymbolicLengthTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded), Is.True);
        Assert.That(encoded, Is.EqualTo(smtFormula));
    }

    [Test]
    public void FormulaEncoder_ArrayDimensionLengthUsesReferenceDimensionLength()
    {
        var array = new SymbolicVariableTerm("matrix#1", SmtValueKind.Reference);
        var dimensionLength = new SymbolicArrayDimensionLengthTerm(array, 1);

        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(dimensionLength, out var encoded), Is.True);
        Assert.That(
            encoded,
            Is.EqualTo(new SmtVariable("matrix#1.GetLength(1)", SmtValueKind.Int)));
    }

    [Test]
    public void LowerTerm_ArrayCreationDimensionLengthUsesSizeExpression()
    {
        var context = CreateExpressionContext(
            "int rows, int columns",
            "new int[rows, columns].GetLength(1) == columns");
        var arrayCreation = context.Expression
            .DescendantNodes()
            .OfType<ArrayCreationExpressionSyntax>()
            .Single();

        Assert.That(
            SymbolicIrLowerer.TryLowerArrayDimensionLengthTerm(arrayCreation, 1, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicVariableTerm>());
        var variable = (SymbolicVariableTerm)term;
        Assert.That(variable.Name, Does.StartWith("columns#"));
        Assert.That(variable.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
    }

    [Test]
    public void LowerTerm_ArrayGetLengthInvocationUsesDimensionLengthTerm()
    {
        var context = CreateExpressionContext(
            "int[,] matrix, int columns",
            "matrix.GetLength(1) == columns");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        var dimensionLength = (SymbolicArrayDimensionLengthTerm)term;
        Assert.That(dimensionLength.Dimension, Is.EqualTo(1));
        Assert.That(dimensionLength.Value, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)dimensionLength.Value).Name, Does.StartWith("matrix#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
    }

    [Test]
    public void LowerTerm_ArrayGetLongLengthInvocationUsesDimensionLengthTerm()
    {
        var context = CreateExpressionContext(
            "int[,] matrix, long columns",
            "matrix.GetLongLength(1) == columns");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        var dimensionLength = (SymbolicArrayDimensionLengthTerm)term;
        Assert.That(dimensionLength.Dimension, Is.EqualTo(1));
        Assert.That(dimensionLength.Value, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)dimensionLength.Value).Name, Does.StartWith("matrix#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
    }

    [Test]
    public void LowerTerm_CastedArrayGetLengthInvocationUsesUnderlyingReferenceTerm()
    {
        var context = CreateExpressionContext(
            "object value, int columns",
            "((int[,])value).GetLength(1) == columns");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        var dimensionLength = (SymbolicArrayDimensionLengthTerm)term;
        Assert.That(dimensionLength.Dimension, Is.EqualTo(1));
        Assert.That(dimensionLength.Value, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)dimensionLength.Value).Name, Does.StartWith("value#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
    }

    [Test]
    public void LowerTerm_ArrayRankMemberUsesStaticArrayRank()
    {
        var context = CreateExpressionContext(
            "int[,] matrix",
            "matrix.Rank == 2");
        var memberAccess = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(memberAccess, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.EqualTo(new SymbolicIntegerConstantTerm(2)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtIntegerConstant(2)));
    }

    [Test]
    public void LowerTerm_MultidimensionalArrayLengthUsesDimensionProduct()
    {
        var context = CreateExpressionContext(
            "int[,] matrix, int total",
            "matrix.Length == total");
        var memberAccess = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(memberAccess, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var multiply = (SymbolicBinaryTerm)term;
        Assert.That(multiply.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Multiply));
        Assert.That(multiply.Left, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        Assert.That(((SymbolicArrayDimensionLengthTerm)multiply.Left).Dimension, Is.EqualTo(0));
        Assert.That(multiply.Right, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        Assert.That(((SymbolicArrayDimensionLengthTerm)multiply.Right).Dimension, Is.EqualTo(1));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_MultidimensionalArrayUsesDimensionProduct()
    {
        var context = CreateExpressionContext(
            "int[,] matrix, int total",
            "matrix.Length == total");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var multiply = (SymbolicBinaryTerm)term;
        Assert.That(multiply.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Multiply));
        Assert.That(multiply.Left, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        Assert.That(multiply.Right, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_StringSubstringOneArgumentUsesSourceLengthDelta()
    {
        var context = CreateExpressionContext(
            "string text, int start",
            "text.Substring(start).Length == text.Length - start");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var subtract = (SymbolicBinaryTerm)term;
        Assert.That(subtract.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Subtract));
        Assert.That(subtract.Left, Is.TypeOf<SymbolicLengthTerm>());
        Assert.That(subtract.Right, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_StringSubstringTwoArgumentUsesRequestedLength()
    {
        var context = CreateExpressionContext(
            "string text, int start, int length",
            "text.Substring(start, length).Length == length");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)term).Name, Does.StartWith("length#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Int));
    }

    [Test]
    public void LowerBuiltInLengthTerm_ArrayRangeUsesEndpointDifference()
    {
        var context = CreateExpressionContext(
            "int[] values",
            "values[1..^1].Length == values.Length - 2");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var subtract = (SymbolicBinaryTerm)term;
        Assert.That(subtract.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Subtract));
        Assert.That(subtract.Left, Is.TypeOf<SymbolicBinaryTerm>());
        Assert.That(subtract.Right, Is.TypeOf<SymbolicIntegerConstantTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_StringRangeUsesEndpointDifference()
    {
        var context = CreateExpressionContext(
            "string text",
            "text[1..^1].Length == text.Length - 2");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var subtract = (SymbolicBinaryTerm)term;
        Assert.That(subtract.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Subtract));
        Assert.That(subtract.Left, Is.TypeOf<SymbolicBinaryTerm>());
        Assert.That(subtract.Right, Is.TypeOf<SymbolicIntegerConstantTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_StringAsSpanOneArgumentUsesSourceLengthDelta()
    {
        var context = CreateMethodExpressionContext(
            "string text, int start",
            string.Empty,
            "text.AsSpan(start).Length == text.Length - start");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var subtract = (SymbolicBinaryTerm)term;
        Assert.That(subtract.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Subtract));
        Assert.That(subtract.Left, Is.TypeOf<SymbolicLengthTerm>());
        Assert.That(subtract.Right, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_ReadOnlySpanSliceTwoArgumentUsesRequestedLength()
    {
        var context = CreateMethodExpressionContext(
            "ReadOnlySpan<int> values, int start, int length",
            string.Empty,
            "values.Slice(start, length).Length == length");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)term).Name, Does.StartWith("length#"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Int));
    }

    [Test]
    public void LowerBuiltInLengthTerm_AssignedRangeElementAccessUsesResolvedEndpoints()
    {
        var context = CreateMethodExpressionContext(
            "int[] values",
            """
            Range range = 1..^1;
            """,
            "values[range].Length == values.Length - 2");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_AssignedRangeStringViewUsesResolvedEndpoints()
    {
        var context = CreateMethodExpressionContext(
            "string text, Range range",
            """
            range = 1..^1;
            """,
            "text.AsSpan(range).Length == text.Length - 2");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context.LoweringContext, out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void StringContentReferenceHelper_CreatesReferenceBackedStringTerm()
    {
        var reference = new SymbolicVariableTerm("text#1", SmtValueKind.Reference);

        Assert.That(SymbolicIrLowerer.TryCreateStringContentReferenceTerm(reference, out var term), Is.True);
        Assert.That(term, Is.EqualTo(new SymbolicStringContentTerm(reference)));
    }

    [Test]
    public void ReachabilityHelper_BuiltInLengthAssignedValueFactSupportsMultidimensionalArrayTargets()
    {
        var context = CreateLocalDeclarationContext(
            "int[,] values",
            "int[,] copy = values;");

        Assert.That(
            SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact(
                context.Symbol,
                context.ValueExpression,
                context.SemanticModel,
                CancellationToken.None,
                out var fact),
            Is.True);

        Assert.That(fact, Is.TypeOf<SmtBinaryFormula>());
        var equality = (SmtBinaryFormula)fact;
        Assert.That(equality.Operator, Is.EqualTo(SmtBinaryOperator.Equal));
        Assert.That(equality.Left, Is.TypeOf<SmtIntegerBinaryTerm>());
        Assert.That(equality.Right.Kind, Is.EqualTo(SmtValueKind.Int));
    }

    [Test]
    public void ReachabilityHelper_BuiltInLengthValueSupportsAssignedRangeStringViews()
    {
        var context = CreateMethodExpressionContext(
            "string text, Range range",
            """
            range = 1..^1;
            """,
            "text.AsSpan(range).Length == text.Length - 2");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicReachabilityService.TryTranslateBuiltInLengthValue(
                memberAccess.Expression,
                context.SemanticModel,
                CancellationToken.None,
                out var formula),
            Is.True);

        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerBuiltInLengthTerm_CountBackedIndexerUsesCountTerm()
    {
        var context = CreateMethodExpressionContext(
            "System.Collections.Generic.IReadOnlyList<int> values",
            string.Empty,
            "values.Count > 0");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(
                memberAccess.Expression,
                context.LoweringContext,
                out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicCountTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(formula.ToString(), Does.Contain(".Count"));
    }

    [Test]
    public void LowerBuiltInLengthTerm_CountOnlyCollectionUsesCountTerm()
    {
        var context = CreateMethodExpressionContext(
            "System.Collections.Generic.IReadOnlyCollection<int> values",
            string.Empty,
            "values.Count > 0");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(
                memberAccess.Expression,
                context.LoweringContext,
                out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicCountTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtVariable>());
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(formula.ToString(), Does.Contain(".Count"));
    }

    [Test]
    public void LowerBuiltInLengthTerm_SpanParameterUsesReferenceBackedLengthTerm()
    {
        var context = CreateExpressionContext(
            "System.Span<int> span",
            "span.Length == 0");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(
                memberAccess.Expression,
                context.LoweringContext,
                out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicLengthTerm>());
        var lengthTerm = (SymbolicLengthTerm)term;
        Assert.That(lengthTerm.Value, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)lengthTerm.Value).Kind, Is.EqualTo(SmtValueKind.Reference));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Int));
    }

    [Test]
    public void LowerBuiltInLengthTerm_CollectionExpressionSpreadUsesSummedLengths()
    {
        var context = CreateMethodLocalDeclarationContext(
            "System.Collections.Generic.IReadOnlyCollection<int> values",
            "int[] copy = [0, .. values, 1];");

        Assert.That(
            SymbolicIrLowerer.TryLowerBuiltInLengthTerm(
                context.ValueExpression,
                new SymbolicLoweringContext(context.SemanticModel, CancellationToken.None),
                out var term),
            Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
        Assert.That(formula.ToString(), Does.Contain(".Count"));
    }

    [Test]
    public void BuiltInElementAccessInRangeCondition_SupportsAssignedIndexShape()
    {
        var context = CreateMethodExpressionContext(
            "int[] values",
            """
            Index index = ^1;
            """,
            "values[index] == 0");
        var elementAccess = (ElementAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(
            SymbolicIrLowerer.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments[0].Expression,
                elementAccess,
                "test.element-access.in-range",
                context.LoweringContext,
                out var condition),
            Is.True);
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void BuiltInElementAccessInRangeCondition_SupportsRangeShape()
    {
        var context = CreateMethodExpressionContext(
            "int[] values",
            """
            Range slice = 1..^1;
            """,
            "values[slice].Length == values.Length - 2");
        var memberAccess = (MemberAccessExpressionSyntax)((BinaryExpressionSyntax)context.Expression).Left;
        var elementAccess = (ElementAccessExpressionSyntax)memberAccess.Expression;

        Assert.That(
            SymbolicIrLowerer.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments[0].Expression,
                elementAccess,
                "test.range-access.in-range",
                context.LoweringContext,
                out var condition),
            Is.True);
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var formula), Is.True);
        Assert.That(formula.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    [Test]
    public void ReachabilityHelper_BuiltInLengthAssignedValueFactSupportsAssignedRangeStringViews()
    {
        var context = CreateMethodLocalDeclarationContext(
            "string text, Range range",
            """
            range = 1..^1;
            ReadOnlySpan<char> view = text.AsSpan(range);
            """,
            "using System;");

        Assert.That(
            SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact(
                context.Symbol,
                context.ValueExpression,
                context.SemanticModel,
                CancellationToken.None,
                out var fact),
            Is.True);

        Assert.That(fact, Is.TypeOf<SmtBinaryFormula>());
        var equality = (SmtBinaryFormula)fact;
        Assert.That(equality.Operator, Is.EqualTo(SmtBinaryOperator.Equal));
        Assert.That(equality.Left.Kind, Is.EqualTo(SmtValueKind.Int));
        Assert.That(equality.Right.Kind, Is.EqualTo(SmtValueKind.Int));
    }

    [Test]
    public void ReachabilityHelper_StringContentAssignedValueFactSupportsStringTargets()
    {
        var context = CreateLocalDeclarationContext(
            "string input",
            "string copy = input;");

        Assert.That(
            SymbolicReachabilityService.TryCreateStringContentAssignedValueFact(
                context.Symbol,
                context.ValueExpression,
                context.SemanticModel,
                CancellationToken.None,
                out var fact),
            Is.True);

        Assert.That(fact, Is.TypeOf<SmtBinaryFormula>());
        var equality = (SmtBinaryFormula)fact;
        Assert.That(equality.Operator, Is.EqualTo(SmtBinaryOperator.Equal));
        Assert.That(equality.Left.Kind, Is.EqualTo(SmtValueKind.String));
        Assert.That(equality.Right.Kind, Is.EqualTo(SmtValueKind.String));
    }

    [Test]
    public void LowerTerm_MultidimensionalArrayCreationLengthUsesSizeProduct()
    {
        var context = CreateExpressionContext(
            "int rows, int columns",
            "new int[rows, columns].Length == rows * columns");
        var memberAccess = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(memberAccess, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var multiply = (SymbolicBinaryTerm)term;
        Assert.That(multiply.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Multiply));
        Assert.That(multiply.Left, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)multiply.Left).Name, Does.StartWith("rows#"));
        Assert.That(multiply.Right, Is.TypeOf<SymbolicVariableTerm>());
        Assert.That(((SymbolicVariableTerm)multiply.Right).Name, Does.StartWith("columns#"));
    }

    [Test]
    public void LowerTerm_ArrayGetLowerBoundInvocationUsesZeroTerm()
    {
        var context = CreateExpressionContext(
            "int[,] matrix",
            "matrix.GetLowerBound(1) == 0");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.EqualTo(new SymbolicIntegerConstantTerm(0)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.EqualTo(new SmtIntegerConstant(0)));
    }

    [Test]
    public void LowerTerm_ArrayGetUpperBoundInvocationUsesDimensionLengthMinusOne()
    {
        var context = CreateExpressionContext(
            "int[,] matrix, int columns",
            "matrix.GetUpperBound(1) == columns - 1");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicBinaryTerm>());
        var subtract = (SymbolicBinaryTerm)term;
        Assert.That(subtract.Operator, Is.EqualTo(SymbolicBinaryTermOperator.Subtract));
        Assert.That(subtract.Left, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        Assert.That(((SymbolicArrayDimensionLengthTerm)subtract.Left).Dimension, Is.EqualTo(1));
        Assert.That(subtract.Right, Is.EqualTo(new SymbolicIntegerConstantTerm(1)));
        Assert.That(SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula), Is.True);
        Assert.That(formula, Is.TypeOf<SmtIntegerBinaryTerm>());
    }

    [Test]
    public void LowerTerm_ArrayCreationGetLengthInvocationUsesSizeExpression()
    {
        var context = CreateExpressionContext(
            "int rows, int columns",
            "new int[rows, columns].GetLength(1) == columns");
        var invocation = ((BinaryExpressionSyntax)context.Expression).Left;

        Assert.That(SymbolicIrLowerer.TryLowerTerm(invocation, context.LoweringContext, out var term), Is.True);

        Assert.That(term, Is.TypeOf<SymbolicVariableTerm>());
        var variable = (SymbolicVariableTerm)term;
        Assert.That(variable.Name, Does.StartWith("columns#"));
        Assert.That(variable.Kind, Is.EqualTo(SmtValueKind.Int));
    }

    [Test]
    public void SmtFormulaLowerer_ArrayDimensionLengthUsesSharedDimensionLengthAtom()
    {
        var sourceNode = SyntaxFactory.ParseExpression("matrix");
        var smtFormula = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            new SmtVariable("matrix#1.GetLength(1)", SmtValueKind.Int),
            new SmtIntegerConstant(2));

        Assert.That(SymbolicSmtFormulaLowerer.TryLowerCondition(
            smtFormula,
            sourceNode,
            "test.smt-lowerer.dimension-length",
            "test.smt-lowerer.dimension-length",
            out var condition), Is.True);
        var atom = AssertFactCondition<SymbolicRelationAtom>(condition);

        Assert.That(atom.Operator, Is.EqualTo(SymbolicRelationOperator.GreaterThanOrEqual));
        Assert.That(atom.Left, Is.TypeOf<SymbolicArrayDimensionLengthTerm>());
        var dimensionLength = (SymbolicArrayDimensionLengthTerm)atom.Left;
        Assert.That(dimensionLength.Dimension, Is.EqualTo(1));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded), Is.True);
        Assert.That(encoded, Is.EqualTo(smtFormula));
    }

    [Test]
    public void SmtFormulaLowerer_AsExpressionImplicationUsesTypeTestAtom()
    {
        var sourceNode = SyntaxFactory.ParseExpression("value as string");
        var source = new SmtVariable("value#1", SmtValueKind.Reference);
        var target = new SmtVariable("text#2", SmtValueKind.Reference);
        var smtFormula = new SmtBinaryFormula(
            SmtBinaryOperator.Or,
            new SmtBinaryFormula(SmtBinaryOperator.Equal, target, new SmtNullConstant()),
            new SmtRuntimeTypeTestFormula(source, "System.String"));

        Assert.That(SymbolicSmtFormulaLowerer.TryLowerCondition(
            smtFormula,
            sourceNode,
            "test.smt-lowerer.as",
            "test.smt-lowerer.as",
            out var condition), Is.True);

        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var disjunction = (SymbolicBinaryCondition)condition;
        Assert.That(disjunction.Operator, Is.EqualTo(SymbolicConditionOperator.Or));
        Assert.That(AssertFactCondition<SymbolicRelationAtom>(disjunction.Left).Right, Is.TypeOf<SymbolicNullTerm>());
        Assert.That(AssertFactCondition<SymbolicTypeTestAtom>(disjunction.Right).TypeKey, Is.EqualTo("System.String"));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded), Is.True);
        Assert.That(encoded, Is.EqualTo(smtFormula));
    }

    [Test]
    public void SmtFormulaLowerer_BooleanEqualityUsesConditionEquivalence()
    {
        var sourceNode = SyntaxFactory.ParseExpression("text");
        var targetNonNull = new SmtBinaryFormula(
            SmtBinaryOperator.NotEqual,
            new SmtVariable("text#1", SmtValueKind.Reference),
            new SmtNullConstant());
        var smtFormula = new SmtBinaryFormula(
            SmtBinaryOperator.Equal,
            targetNonNull,
            new SmtBooleanConstant(true));

        Assert.That(SymbolicSmtFormulaLowerer.TryLowerCondition(
            smtFormula,
            sourceNode,
            "test.smt-lowerer.bool-equality",
            "test.smt-lowerer.bool-equality",
            out var condition), Is.True);

        Assert.That(condition, Is.TypeOf<SymbolicBinaryCondition>());
        var equivalence = (SymbolicBinaryCondition)condition;
        Assert.That(equivalence.Operator, Is.EqualTo(SymbolicConditionOperator.Or));
        Assert.That(SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded), Is.True);
        Assert.That(encoded.Kind, Is.EqualTo(SmtValueKind.Bool));
    }

    private static TAtom AssertFactCondition<TAtom>(SymbolicCondition condition)
        where TAtom : SymbolicAtom
    {
        Assert.That(condition, Is.TypeOf<SymbolicFactCondition>());
        var factCondition = (SymbolicFactCondition)condition;
        Assert.That(factCondition.Fact.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact));
        Assert.That(factCondition.Fact.Atom, Is.TypeOf<TAtom>());
        return (TAtom)factCondition.Fact.Atom;
    }

    private static ExpressionContext CreateExpressionContext(string parameters, string expression,
        string declarations = "")
    {
        var source = $$"""
                       {{declarations}}
                       public sealed class C
                       {
                           public bool M({{parameters}})
                           {
                               return {{expression}};
                           }
                       }
                       """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SymbolicIrTest",
            new[] { syntaxTree },
            AnalyzerTestHost.GetMinimalFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var returnStatement = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single();

        return new ExpressionContext(
            returnStatement.Expression!,
            semanticModel,
            new SymbolicLoweringContext(semanticModel, CancellationToken.None));
    }

    private static ExpressionContext CreateMethodExpressionContext(string parameters, string statements,
        string expression)
    {
        var source = $$"""
                       using System;
                       public sealed class C
                       {
                           public bool M({{parameters}})
                           {
                               {{statements}}
                               return {{expression}};
                           }
                       }
                       """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SymbolicIrMethodExpressionTest",
            new[] { syntaxTree },
            AnalyzerTestHost.GetMinimalFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var returnStatement = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single();

        return new ExpressionContext(
            returnStatement.Expression!,
            semanticModel,
            new SymbolicLoweringContext(semanticModel, CancellationToken.None));
    }

    private static LocalDeclarationContext CreateLocalDeclarationContext(string parameters, string declaration,
        string declarations = "")
    {
        var source = $$"""
                       {{declarations}}
                       public sealed class C
                       {
                           public void M({{parameters}})
                           {
                               {{declaration}}
                           }
                       }
                       """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SymbolicIrLocalDeclarationTest",
            new[] { syntaxTree },
            AnalyzerTestHost.GetMinimalFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var declarator = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single();

        return new LocalDeclarationContext(
            (ILocalSymbol)semanticModel.GetDeclaredSymbol(declarator)!,
            declarator.Initializer!.Value,
            semanticModel);
    }

    private static LocalDeclarationContext CreateMethodLocalDeclarationContext(string parameters, string statements,
        string declarations = "")
    {
        var source = $$"""
                       {{declarations}}
                       public sealed class C
                       {
                           public void M({{parameters}})
                           {
                               {{statements}}
                           }
                       }
                       """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SymbolicIrMethodLocalDeclarationTest",
            new[] { syntaxTree },
            AnalyzerTestHost.GetMinimalFrameworkReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var declarator = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single();

        return new LocalDeclarationContext(
            (ILocalSymbol)semanticModel.GetDeclaredSymbol(declarator)!,
            declarator.Initializer!.Value,
            semanticModel);
    }

    private sealed record ExpressionContext(
        ExpressionSyntax Expression,
        SemanticModel SemanticModel,
        SymbolicLoweringContext LoweringContext);

    private sealed record LocalDeclarationContext(
        ILocalSymbol Symbol,
        ExpressionSyntax ValueExpression,
        SemanticModel SemanticModel);
}