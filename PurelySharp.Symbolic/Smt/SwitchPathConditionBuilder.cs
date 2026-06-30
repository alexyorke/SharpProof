using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

                    if (!TryCreateSwitchLabelSelectionCondition(
                            governingExpression,
                            label,
                            semanticModel,
                            cancellationToken,
                            out var labelCondition,
                            getSymbolVersion))
                    {
                        continue;
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

                    if (!TryCreateSwitchLabelSelectionCondition(
                            governingExpression,
                            label,
                            semanticModel,
                            cancellationToken,
                            out var labelCondition,
                            getSymbolVersion))
                    {
                        continue;
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

        private static bool TryCreateSwitchLabelSelectionCondition(
            ExpressionSyntax governingExpression,
            SwitchLabelSyntax label,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null!;
            var governingType = GetExpressionType(governingExpression, semanticModel, cancellationToken);
            if (!TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken, out var governingValue, getSymbolVersion))
            {
                return false;
            }

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
                formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, governingValue, caseValue);
                return true;
            }

            if (label is CasePatternSwitchLabelSyntax patternLabel)
            {
                return TryCreatePatternAndGuardSelectionCondition(
                    governingValue,
                    governingType,
                    patternLabel.Pattern,
                    patternLabel.WhenClause,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion);
            }

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

                if (!TryCreatePatternAndGuardSelectionCondition(
                        governingValue,
                        governingType,
                        arm.Pattern,
                        arm.WhenClause,
                        semanticModel,
                        cancellationToken,
                        out var priorArmCondition,
                        getSymbolVersion))
                {
                    continue;
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

        private static bool TryCreatePatternAndGuardSelectionCondition(
            SmtFormula governingValue,
            ITypeSymbol? governingType,
            PatternSyntax pattern,
            WhenClauseSyntax? whenClause,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null!;
            if (!CSharpConditionToFormula.TryTranslatePattern(
                    governingValue,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var patternFormula,
                    getSymbolVersion,
                    governingType) ||
                patternFormula == null)
            {
                return false;
            }

            var conditions = new List<SmtFormula> { patternFormula };
            if (whenClause != null)
            {
                if (!CSharpConditionToFormula.TryTranslate(
                        whenClause.Condition,
                        semanticModel,
                        cancellationToken,
                        out var guardFormula,
                        getSymbolVersion) ||
                    guardFormula == null)
                {
                    return false;
                }

                conditions.Add(guardFormula);
            }

            return TryCreateConjunction(conditions, out formula);
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
                AddStructuralPatternFacts(
                    governingValue,
                    governingType,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion,
                    includeWholePatternTranslation: false);
            }
            else
            {
                AddStructuralPatternFacts(
                    governingValue,
                    governingType,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion,
                    includeWholePatternTranslation: false);
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

        private static void AddStructuralPatternFacts(
            SmtFormula value,
            ITypeSymbol? valueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> conditions,
            Func<ISymbol, int>? getSymbolVersion,
            bool includeWholePatternTranslation = true)
        {
            if (includeWholePatternTranslation &&
                CSharpConditionToFormula.TryTranslatePattern(
                    value,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var patternFormula,
                    getSymbolVersion,
                    valueType) &&
                patternFormula != null)
            {
                conditions.Add(patternFormula);
                return;
            }

            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                AddStructuralPatternFacts(
                    value,
                    valueType,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
                return;
            }

            if (pattern is DeclarationPatternSyntax or TypePatternSyntax)
            {
                AddReferenceNonNullFact(value, conditions);
                return;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
            {
                AddStructuralPatternFacts(
                    value,
                    valueType,
                    binaryPattern.Left,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
                AddStructuralPatternFacts(
                    value,
                    valueType,
                    binaryPattern.Right,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
                return;
            }

            if (pattern is ListPatternSyntax listPattern)
            {
                AddListPatternStructuralFacts(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
                return;
            }

            if (pattern is RecursivePatternSyntax recursivePattern)
            {
                AddRecursivePatternStructuralFacts(
                    value,
                    valueType,
                    recursivePattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
                return;
            }
        }

        private static void AddRecursivePatternStructuralFacts(
            SmtFormula value,
            ITypeSymbol? valueType,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> conditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (value.Kind == SmtValueKind.Reference &&
                (valueType == null || valueType.IsReferenceType))
            {
                AddReferenceNonNullFact(value, conditions);
            }

            var propertySubpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (propertySubpatterns != null)
            {
                foreach (var subpattern in propertySubpatterns.Value)
                {
                    if (!TryResolvePropertySubpatternValue(
                            value,
                            subpattern,
                            semanticModel,
                            cancellationToken,
                            out var memberValue,
                            out var memberType,
                            out var pathCondition) ||
                        memberValue == null ||
                        memberType == null)
                    {
                        continue;
                    }

                    if (pathCondition != null)
                    {
                        conditions.Add(pathCondition);
                    }

                    AddStructuralPatternFacts(
                        memberValue,
                        memberType,
                        subpattern.Pattern,
                        semanticModel,
                        cancellationToken,
                        conditions,
                        getSymbolVersion);
                }
            }

            var positionalSubpatterns = recursivePattern.PositionalPatternClause?.Subpatterns;
            if (positionalSubpatterns == null)
            {
                return;
            }

            for (var position = 0; position < positionalSubpatterns.Value.Count; position++)
            {
                if (!TryResolveTuplePositionalSubpatternValue(
                        value,
                        valueType,
                        position,
                        out var memberValue,
                        out var memberType) ||
                    memberValue == null ||
                    memberType == null)
                {
                    continue;
                }

                AddStructuralPatternFacts(
                    memberValue,
                    memberType,
                    positionalSubpatterns.Value[position].Pattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
            }
        }

        private static void AddListPatternStructuralFacts(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> conditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (value.Kind != SmtValueKind.Reference ||
                !TryCreateListPatternLengthFormula(value, valueType, semanticModel, out var lengthFormula))
            {
                return;
            }

            AddReferenceNonNullFact(value, conditions);

            var hasSlice = false;
            var minimumLength = 0;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        minimumLength += GetListPatternMinimumLength(nestedListPattern);
                    }

                    hasSlice = true;
                    continue;
                }

                minimumLength++;
            }

            conditions.Add(hasSlice
                ? new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength))
                : new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength)));

            if (!TryGetBuiltInListPatternElementType(valueType, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return;
            }

            for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
            {
                var subpattern = listPattern.Patterns[patternIndex];
                if (subpattern is SlicePatternSyntax)
                {
                    continue;
                }

                if (!TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex, out var fromEnd))
                {
                    continue;
                }

                var elementValue = CreateListPatternElementFormula(value, elementIndex, fromEnd, elementKind);
                AddStructuralPatternFacts(
                    elementValue,
                    elementType,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
            }
        }

        private static bool TryCreateListPatternLengthFormula(
            SmtFormula value,
            ITypeSymbol? valueType,
            SemanticModel semanticModel,
            out SmtFormula lengthFormula)
        {
            lengthFormula = null!;
            if (!IsSupportedBuiltInListPatternReceiver(valueType))
            {
                return false;
            }

            if (valueType?.SpecialType == SpecialType.System_String)
            {
                return TryCreateStringLengthFormula(value, out lengthFormula);
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (!TryCreateMemberFormula(value, "Length", intType, out var memberFormula) ||
                memberFormula == null)
            {
                return false;
            }

            lengthFormula = memberFormula;
            return true;
        }

        private static bool IsSupportedBuiltInListPatternReceiver(ITypeSymbol? valueType)
        {
            return valueType is IArrayTypeSymbol { Rank: 1 } ||
                valueType?.SpecialType == SpecialType.System_String;
        }

        private static bool TryGetBuiltInListPatternElementType(ITypeSymbol? valueType, out ITypeSymbol elementType)
        {
            if (valueType is IArrayTypeSymbol { Rank: 1 } arrayType)
            {
                elementType = arrayType.ElementType;
                return true;
            }

            elementType = null!;
            return false;
        }

        private static bool TryGetListPatternElementPosition(
            ListPatternSyntax listPattern,
            int patternIndex,
            out int elementIndex,
            out bool fromEnd)
        {
            elementIndex = 0;
            fromEnd = false;

            if (listPattern.Patterns[patternIndex] is SlicePatternSyntax)
            {
                return false;
            }

            var sliceIndex = -1;
            for (var index = 0; index < listPattern.Patterns.Count; index++)
            {
                if (listPattern.Patterns[index] is SlicePatternSyntax)
                {
                    sliceIndex = index;
                    break;
                }
            }

            if (sliceIndex < 0 || patternIndex < sliceIndex)
            {
                elementIndex = patternIndex;
                return true;
            }

            elementIndex = listPattern.Patterns.Count - patternIndex;
            fromEnd = true;
            return true;
        }

        private static SmtFormula CreateListPatternElementFormula(
            SmtFormula receiver,
            int elementIndex,
            bool fromEnd,
            SmtValueKind elementKind)
        {
            var indexText = fromEnd
                ? "^" + elementIndex.ToString(CultureInfo.InvariantCulture)
                : elementIndex.ToString(CultureInfo.InvariantCulture);
            return new SmtVariable(receiver + "[" + indexText + "]", elementKind);
        }

        private static int GetListPatternMinimumLength(ListPatternSyntax listPattern)
        {
            var minimumLength = 0;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        minimumLength += GetListPatternMinimumLength(nestedListPattern);
                    }

                    continue;
                }

                minimumLength++;
            }

            return minimumLength;
        }

        private static bool TryGetNestedListPattern(PatternSyntax? pattern, out ListPatternSyntax listPattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            if (pattern is ListPatternSyntax candidate)
            {
                listPattern = candidate;
                return true;
            }

            listPattern = null!;
            return false;
        }

        private static bool TryResolveTuplePositionalSubpatternValue(
            SmtFormula receiver,
            ITypeSymbol? receiverType,
            int position,
            out SmtFormula? memberValue,
            out ITypeSymbol? memberType)
        {
            memberValue = null;
            memberType = null;
            if (!TryGetTuplePositionalField(receiverType, position, out var fieldSymbol) ||
                !TryGetTupleElementStorageName(fieldSymbol, out var storageName) ||
                !TryGetValueKind(fieldSymbol.Type, out var kind))
            {
                return false;
            }

            memberType = fieldSymbol.Type;
            memberValue = new SmtVariable(GetFormulaVariableName(receiver) + "." + storageName, kind);
            return true;
        }

        private static bool TryGetTuplePositionalField(
            ITypeSymbol? receiverType,
            int position,
            out IFieldSymbol fieldSymbol)
        {
            fieldSymbol = null!;
            if (receiverType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            if (namedType.IsTupleType)
            {
                if (position < 0 || position >= namedType.TupleElements.Length)
                {
                    return false;
                }

                fieldSymbol = namedType.TupleElements[position];
                return true;
            }

            var storageName = "Item" + (position + 1).ToString(CultureInfo.InvariantCulture);
            fieldSymbol = namedType
                .GetMembers(storageName)
                .OfType<IFieldSymbol>()
                .FirstOrDefault(static field => !field.IsStatic)!;
            return fieldSymbol != null;
        }

        private static bool TryGetTupleElementStorageName(IFieldSymbol fieldSymbol, out string storageName)
        {
            var tupleField = fieldSymbol.CorrespondingTupleField ?? fieldSymbol;
            if (IsTupleElementStorageName(tupleField.Name))
            {
                storageName = tupleField.Name;
                return true;
            }

            storageName = string.Empty;
            return false;
        }

        private static bool IsTupleElementStorageName(string name)
        {
            return name.Length > 4 &&
                name.StartsWith("Item", StringComparison.Ordinal) &&
                name.Skip(4).All(char.IsDigit);
        }

        private static void AddReferenceNonNullFact(
            SmtFormula value,
            ICollection<SmtFormula> conditions)
        {
            if (value.Kind != SmtValueKind.Reference)
            {
                return;
            }

            conditions.Add(new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                value,
                new SmtNullConstant()));
        }

        private static string GetFormulaVariableName(SmtFormula formula)
        {
            return formula is SmtVariable variable
                ? variable.Name
                : formula.ToString() ?? string.Empty;
        }

        private static bool TryResolvePropertySubpatternValue(
            SmtFormula receiver,
            SubpatternSyntax subpattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? memberValue,
            out ITypeSymbol? memberType,
            out SmtFormula? pathCondition)
        {
            memberValue = null;
            memberType = null;
            pathCondition = null;

            var propertyPath = (SyntaxNode?)subpattern.NameColon?.Name ?? subpattern.ExpressionColon?.Expression;
            if (propertyPath == null ||
                !TryGetPropertySubpatternMemberNames(propertyPath, out var memberNames))
            {
                return false;
            }

            var currentValue = receiver;
            for (var index = 0; index < memberNames.Count; index++)
            {
                var memberName = memberNames[index];
                var memberSymbol = semanticModel.GetSymbolInfo(memberName, cancellationToken).Symbol;
                if (!TryGetMemberType(memberSymbol, out memberType))
                {
                    return false;
                }

                SmtFormula? nextValue;
                if (memberSymbol?.Name == "Length" &&
                    memberSymbol.ContainingType?.SpecialType == SpecialType.System_String &&
                    TryCreateStringLengthFormula(currentValue, out var stringLengthFormula))
                {
                    memberType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                    nextValue = stringLengthFormula;
                }
                else if (memberSymbol == null ||
                         !TryCreateMemberFormula(currentValue, memberSymbol.Name, memberType, out nextValue) ||
                         nextValue == null)
                {
                    return false;
                }

                currentValue = nextValue;
                if (index < memberNames.Count - 1 &&
                    memberType.IsReferenceType)
                {
                    var nonNull = new SmtBinaryFormula(
                        SmtBinaryOperator.NotEqual,
                        currentValue,
                        new SmtNullConstant());
                    pathCondition = pathCondition == null
                        ? nonNull
                        : new SmtBinaryFormula(SmtBinaryOperator.And, pathCondition, nonNull);
                }
            }

            memberValue = currentValue;
            return memberType != null;
        }

        private static bool TryGetPropertySubpatternMemberNames(
            SyntaxNode propertyPath,
            out List<SimpleNameSyntax> memberNames)
        {
            memberNames = new List<SimpleNameSyntax>();
            return AddPropertySubpatternMemberNames(propertyPath, memberNames) &&
                memberNames.Count > 0;
        }

        private static bool AddPropertySubpatternMemberNames(
            SyntaxNode propertyPath,
            ICollection<SimpleNameSyntax> memberNames)
        {
            switch (propertyPath)
            {
                case SimpleNameSyntax simpleName:
                    memberNames.Add(simpleName);
                    return true;
                case QualifiedNameSyntax qualifiedName:
                    return AddPropertySubpatternMemberNames(qualifiedName.Left, memberNames) &&
                        AddPropertySubpatternMemberNames(qualifiedName.Right, memberNames);
                case MemberAccessExpressionSyntax memberAccess:
                    return AddPropertySubpatternMemberNames(memberAccess.Expression, memberNames) &&
                        AddPropertySubpatternMemberNames(memberAccess.Name, memberNames);
                default:
                    return false;
            }
        }

        private static bool TryCreateStringLengthFormula(SmtFormula receiver, out SmtFormula formula)
        {
            formula = null!;
            if (receiver.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var receiverName = receiver is SmtVariable variable
                ? variable.Name
                : receiver.ToString();
            if (string.IsNullOrEmpty(receiverName))
            {
                return false;
            }

            formula = new SmtStringLengthTerm(new SmtVariable(receiverName + ".String", SmtValueKind.String));
            return true;
        }

        private static bool TryCreateMemberFormula(
            SmtFormula receiver,
            string memberName,
            ITypeSymbol type,
            out SmtFormula? formula)
        {
            formula = null;
            var variableName = receiver + "." + memberName;
            if (TryGetValueKind(type, out var kind))
            {
                formula = new SmtVariable(variableName, kind);
                return true;
            }

            return false;
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (type.IsReferenceType ||
                IsSupportedTupleCarrierType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool IsSupportedTupleCarrierType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            if (namedType.IsTupleType && namedType.TupleElements.Length > 0)
            {
                return true;
            }

            return namedType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Any(static field => !field.IsStatic && IsTupleElementStorageName(field.Name));
        }

        private static bool TryGetMemberType(ISymbol? memberSymbol, out ITypeSymbol type)
        {
            switch (memberSymbol)
            {
                case IPropertySymbol propertySymbol:
                    type = propertySymbol.Type;
                    return true;
                case IFieldSymbol fieldSymbol:
                    type = fieldSymbol.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 ||
                typeSymbol.TypeKind == TypeKind.Enum;
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
