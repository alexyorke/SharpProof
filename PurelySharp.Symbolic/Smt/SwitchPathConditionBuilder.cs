using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Smt
{
    public static class SwitchPathConditionBuilder
    {
        public static bool TryCreateSwitchStatementSectionCondition(
            ExpressionSyntax governingExpression,
            SwitchSectionSyntax section,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            var labelConditions = new List<SmtFormula>();
            var hasDefaultLabel = false;
            foreach (var label in section.Labels)
            {
                if (label is DefaultSwitchLabelSyntax)
                {
                    hasDefaultLabel = true;
                    continue;
                }

                if (TryCreateSwitchLabelCondition(
                    governingExpression,
                    label,
                    semanticModel,
                    cancellationToken,
                    out var labelCondition))
                {
                    labelConditions.Add(labelCondition);
                }
                else
                {
                    return false;
                }
            }

            if (hasDefaultLabel)
            {
                if (!TryCreateSwitchDefaultCondition(
                        governingExpression,
                        section,
                        semanticModel,
                        cancellationToken,
                        out var defaultCondition))
                {
                    return false;
                }

                labelConditions.Add(defaultCondition);
            }

            return TryCreateDisjunction(labelConditions, out formula);
        }

        public static bool TryCreateSwitchExpressionArmCondition(
            ExpressionSyntax governingExpression,
            SwitchExpressionArmSyntax arm,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            var governingType = GetExpressionType(governingExpression, semanticModel, cancellationToken);
            var domainFacts = new List<SmtFormula>();
            CSharpConditionToFormula.TryCollectDomainFacts(
                governingExpression,
                semanticModel,
                cancellationToken,
                domainFacts);

            return TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken, out var governingValue) &&
                TryCreatePatternAndGuardCondition(
                    governingValue,
                    governingType,
                    arm.Pattern,
                    arm.WhenClause,
                    semanticModel,
                    cancellationToken,
                    domainFacts,
                    out formula);
        }

        private static bool TryCreateSwitchLabelCondition(
            ExpressionSyntax governingExpression,
            SwitchLabelSyntax label,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            var governingType = GetExpressionType(governingExpression, semanticModel, cancellationToken);
            if (!TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken, out var governingValue))
            {
                return false;
            }

            var domainFacts = new List<SmtFormula>();
            CSharpConditionToFormula.TryCollectDomainFacts(
                governingExpression,
                semanticModel,
                cancellationToken,
                domainFacts);

            if (label is CaseSwitchLabelSyntax caseLabel &&
                CSharpConditionToFormula.TryTranslateValue(
                    caseLabel.Value,
                    semanticModel,
                    cancellationToken,
                    out var caseValue,
                    getSymbolVersion: null) &&
                caseValue != null &&
                AreComparableSmtValues(governingValue, caseValue))
            {
                domainFacts.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, governingValue, caseValue));
                return TryCreateConjunction(domainFacts, out formula);
            }

            if (label is CasePatternSwitchLabelSyntax patternLabel)
            {
                return TryCreatePatternAndGuardCondition(
                    governingValue,
                    governingType,
                    patternLabel.Pattern,
                    patternLabel.WhenClause,
                    semanticModel,
                    cancellationToken,
                    domainFacts,
                    out formula);
            }

            return false;
        }

        private static bool TryCreateSwitchDefaultCondition(
            ExpressionSyntax governingExpression,
            SwitchSectionSyntax defaultSection,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            if (defaultSection.Parent is not SwitchStatementSyntax switchStatement)
            {
                return false;
            }

            var nonDefaultLabelConditions = new List<SmtFormula>();
            foreach (var section in switchStatement.Sections)
            {
                foreach (var label in section.Labels)
                {
                    if (label is DefaultSwitchLabelSyntax)
                    {
                        continue;
                    }

                    if (!TryCreateSwitchLabelCondition(
                            governingExpression,
                            label,
                            semanticModel,
                            cancellationToken,
                            out var labelCondition))
                    {
                        return false;
                    }

                    nonDefaultLabelConditions.Add(labelCondition);
                }
            }

            if (nonDefaultLabelConditions.Count == 0)
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            return TryCreateDisjunction(nonDefaultLabelConditions, out var explicitCaseCondition) &&
                CreateNegation(explicitCaseCondition, out formula);
        }

        private static bool TryTranslateSwitchGoverningValue(
            ExpressionSyntax governingExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (CSharpConditionToFormula.TryTranslateValue(
                    governingExpression,
                    semanticModel,
                    cancellationToken,
                    out var governingValue,
                    getSymbolVersion: null) &&
                governingValue != null &&
                governingValue.Kind is SmtValueKind.Bool or SmtValueKind.Int or SmtValueKind.Reference)
            {
                formula = governingValue;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreatePatternAndGuardCondition(
            SmtFormula governingValue,
            ITypeSymbol? governingType,
            PatternSyntax pattern,
            WhenClauseSyntax? whenClause,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula>? initialConditions,
            out SmtFormula formula)
        {
            formula = null!;
            var conditions = initialConditions == null
                ? new List<SmtFormula>()
                : new List<SmtFormula>(initialConditions);
            if (CSharpConditionToFormula.TryTranslatePattern(
                    governingValue,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var patternFormula,
                    getSymbolVersion: null,
                    governingType) &&
                patternFormula != null)
            {
                conditions.Add(patternFormula);
            }

            CSharpConditionToFormula.TryCollectPatternBindingFacts(
                governingValue,
                governingType,
                pattern,
                semanticModel,
                cancellationToken,
                conditions);

            if (whenClause != null)
            {
                CSharpConditionToFormula.TryCollectDomainFacts(
                    whenClause.Condition,
                    semanticModel,
                    cancellationToken,
                    conditions);

                if (CSharpConditionToFormula.TryTranslate(
                    whenClause.Condition,
                    semanticModel,
                    cancellationToken,
                    out var guardFormula,
                    getSymbolVersion: null) &&
                    guardFormula != null)
                {
                    conditions.Add(guardFormula);
                }
            }

            return TryCreateConjunction(conditions, out formula);
        }

        private static ITypeSymbol? GetExpressionType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.ConvertedType ?? typeInfo.Type;
        }

        private static bool TryCreateConjunction(IReadOnlyList<SmtFormula> formulas, out SmtFormula formula)
        {
            return TryCreateAssociativeFormula(SmtBinaryOperator.And, formulas, out formula);
        }

        private static bool TryCreateDisjunction(IReadOnlyList<SmtFormula> formulas, out SmtFormula formula)
        {
            return TryCreateAssociativeFormula(SmtBinaryOperator.Or, formulas, out formula);
        }

        private static bool CreateNegation(SmtFormula operand, out SmtFormula formula)
        {
            formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
            return true;
        }

        private static bool TryCreateAssociativeFormula(
            SmtBinaryOperator smtOperator,
            IReadOnlyList<SmtFormula> formulas,
            out SmtFormula formula)
        {
            formula = null!;
            if (formulas.Count == 0)
            {
                return false;
            }

            formula = formulas[0];
            for (var index = 1; index < formulas.Count; index++)
            {
                formula = new SmtBinaryFormula(smtOperator, formula, formulas[index]);
            }

            return true;
        }

        private static bool AreComparableSmtValues(SmtFormula left, SmtFormula right)
        {
            return left.Kind == right.Kind ||
                left is SmtNullConstant && right.Kind == SmtValueKind.Reference ||
                right is SmtNullConstant && left.Kind == SmtValueKind.Reference;
        }
    }
}
