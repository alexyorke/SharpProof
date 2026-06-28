using System.Collections.Generic;
using System.Linq;
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
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            bool includePatternBindings = true)
        {
            formula = null!;
            var labelConditions = new List<SmtFormula>();
            var hasDefaultLabel = false;
            var sectionConditions = new List<SmtFormula>();
            if (!TryAddPriorSwitchStatementSectionExclusions(
                    governingExpression,
                    section,
                    semanticModel,
                    cancellationToken,
                    sectionConditions,
                    getSymbolVersion))
            {
                return false;
            }

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
                    includePatternBindings,
                    out var labelCondition,
                    getSymbolVersion))
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
                        out var defaultCondition,
                        getSymbolVersion))
                {
                    return false;
                }

                labelConditions.Add(defaultCondition);
            }

            return TryCreateDisjunction(labelConditions, out var sectionLabelCondition) &&
                TryCreateConjunction(sectionConditions.Concat(new[] { sectionLabelCondition }).ToArray(), out formula);
        }

        public static bool TryCreateSwitchExpressionArmCondition(
            ExpressionSyntax governingExpression,
            SwitchExpressionArmSyntax arm,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            var governingType = GetExpressionType(governingExpression, semanticModel, cancellationToken);
            var domainFacts = new List<SmtFormula>();
            CSharpConditionToFormula.TryCollectDomainFacts(
                governingExpression,
                semanticModel,
                cancellationToken,
                domainFacts,
                getSymbolVersion);

            return TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken, out var governingValue, getSymbolVersion) &&
                TryAddPriorSwitchExpressionArmExclusions(
                    governingValue,
                    governingType,
                    arm,
                    semanticModel,
                    cancellationToken,
                    domainFacts,
                    getSymbolVersion) &&
                TryCreatePatternAndGuardCondition(
                    governingValue,
                    governingType,
                    arm.Pattern,
                    arm.WhenClause,
                    semanticModel,
                    cancellationToken,
                    domainFacts,
                    includePatternBindings: true,
                    out formula,
                    getSymbolVersion);
        }

        private static bool TryCreateSwitchLabelCondition(
            ExpressionSyntax governingExpression,
            SwitchLabelSyntax label,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return TryCreateSwitchLabelCondition(
                governingExpression,
                label,
                semanticModel,
                cancellationToken,
                includePatternBindings: true,
                out formula,
                getSymbolVersion);
        }

        private static bool TryCreateSwitchDefaultCondition(
            ExpressionSyntax governingExpression,
            SwitchSectionSyntax defaultSection,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
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
                            includePatternBindings: false,
                            out var labelCondition,
                            getSymbolVersion))
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

        private static bool TryAddPriorSwitchStatementSectionExclusions(
            ExpressionSyntax governingExpression,
            SwitchSectionSyntax currentSection,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> conditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (currentSection.Parent is not SwitchStatementSyntax switchStatement)
            {
                return true;
            }

            var priorLabelConditions = new List<SmtFormula>();
            foreach (var section in switchStatement.Sections)
            {
                if (ReferenceEquals(section, currentSection))
                {
                    break;
                }

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
                            includePatternBindings: false,
                            out var labelCondition,
                            getSymbolVersion))
                    {
                        return false;
                    }

                    priorLabelConditions.Add(labelCondition);
                }
            }

            if (priorLabelConditions.Count == 0)
            {
                return true;
            }

            if (!TryCreateDisjunction(priorLabelConditions, out var priorLabelDisjunction))
            {
                return false;
            }

            conditions.Add(new SmtUnaryFormula(SmtUnaryOperator.Not, priorLabelDisjunction));
            return true;
        }

        private static bool TryTranslateSwitchGoverningValue(
            ExpressionSyntax governingExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (CSharpConditionToFormula.TryTranslateValue(
                    governingExpression,
                    semanticModel,
                    cancellationToken,
                    out var governingValue,
                    getSymbolVersion) &&
                governingValue != null &&
                governingValue.Kind is SmtValueKind.Bool or SmtValueKind.Int or SmtValueKind.Reference)
            {
                formula = governingValue;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateSwitchLabelCondition(
            ExpressionSyntax governingExpression,
            SwitchLabelSyntax label,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includePatternBindings,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null!;
            var governingType = GetExpressionType(governingExpression, semanticModel, cancellationToken);
            if (!TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken, out var governingValue, getSymbolVersion))
            {
                return false;
            }

            var domainFacts = new List<SmtFormula>();
            CSharpConditionToFormula.TryCollectDomainFacts(
                governingExpression,
                semanticModel,
                cancellationToken,
                domainFacts,
                getSymbolVersion);

            if (label is CaseSwitchLabelSyntax caseLabel &&
                CSharpConditionToFormula.TryTranslateValue(
                    caseLabel.Value,
                    semanticModel,
                    cancellationToken,
                    out var caseValue,
                    getSymbolVersion) &&
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
                    includePatternBindings,
                    out formula,
                    getSymbolVersion);
            }

            return false;
        }

        private static bool TryAddPriorSwitchExpressionArmExclusions(
            SmtFormula governingValue,
            ITypeSymbol? governingType,
            SwitchExpressionArmSyntax currentArm,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> conditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (currentArm.Parent is not SwitchExpressionSyntax switchExpression)
            {
                return true;
            }

            var priorArmConditions = new List<SmtFormula>();
            foreach (var arm in switchExpression.Arms)
            {
                if (ReferenceEquals(arm, currentArm))
                {
                    break;
                }

                if (!TryCreatePatternAndGuardCondition(
                        governingValue,
                        governingType,
                        arm.Pattern,
                        arm.WhenClause,
                        semanticModel,
                        cancellationToken,
                        initialConditions: null,
                        includePatternBindings: false,
                        out var priorArmCondition,
                        getSymbolVersion))
                {
                    return false;
                }

                priorArmConditions.Add(priorArmCondition);
            }

            if (priorArmConditions.Count == 0)
            {
                return true;
            }

            if (!TryCreateDisjunction(priorArmConditions, out var priorArmDisjunction))
            {
                return false;
            }

            conditions.Add(new SmtUnaryFormula(SmtUnaryOperator.Not, priorArmDisjunction));
            return true;
        }

        private static bool TryCreatePatternAndGuardCondition(
            SmtFormula governingValue,
            ITypeSymbol? governingType,
            PatternSyntax pattern,
            WhenClauseSyntax? whenClause,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula>? initialConditions,
            bool includePatternBindings,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
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
                    getSymbolVersion,
                    governingType) &&
                patternFormula != null)
            {
                conditions.Add(patternFormula);
            }

            if (includePatternBindings)
            {
                CSharpConditionToFormula.TryCollectPatternBindingFacts(
                    governingValue,
                    governingType,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
            }

            if (whenClause != null)
            {
                CSharpConditionToFormula.TryCollectDomainFacts(
                    whenClause.Condition,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);

                if (CSharpConditionToFormula.TryTranslate(
                    whenClause.Condition,
                    semanticModel,
                    cancellationToken,
                    out var guardFormula,
                    getSymbolVersion) &&
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
