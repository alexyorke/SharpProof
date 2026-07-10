using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt;

internal static class SwitchPathConditionBuilder
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
            return false;

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
                labelConditions.Add(labelCondition);
            else
                return false;
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
                return false;

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
        SymbolicReachabilityService.TryCollectDomainFacts(
            governingExpression,
            semanticModel,
            cancellationToken,
            domainFacts,
            getSymbolVersion);

        return TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken,
                   out var governingValue, getSymbolVersion) &&
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
                   true,
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
        if (defaultSection.Parent is not SwitchStatementSyntax switchStatement) return false;

        var nonDefaultLabelConditions = new List<SmtFormula>();
        foreach (var section in switchStatement.Sections)
            foreach (var label in section.Labels)
            {
                if (label is DefaultSwitchLabelSyntax) continue;

                if (!TryCreateSwitchLabelSelectionCondition(
                        governingExpression,
                        label,
                        semanticModel,
                        cancellationToken,
                        out var labelCondition,
                        getSymbolVersion))
                    continue;

                nonDefaultLabelConditions.Add(labelCondition);
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
        if (currentSection.Parent is not SwitchStatementSyntax switchStatement) return true;

        var priorLabelConditions = new List<SmtFormula>();
        foreach (var section in switchStatement.Sections)
        {
            if (ReferenceEquals(section, currentSection)) break;

            foreach (var label in section.Labels)
            {
                if (label is DefaultSwitchLabelSyntax) continue;

                if (!TryCreateSwitchLabelSelectionCondition(
                        governingExpression,
                        label,
                        semanticModel,
                        cancellationToken,
                        out var labelCondition,
                        getSymbolVersion))
                    continue;

                priorLabelConditions.Add(labelCondition);
            }
        }

        if (priorLabelConditions.Count == 0) return true;

        if (!TryCreateDisjunction(priorLabelConditions, out var priorLabelDisjunction)) return false;

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
        if (SymbolicReachabilityService.TryTranslateValue(
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
        if (!TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken,
                out var governingValue, getSymbolVersion)) return false;

        if (label is CaseSwitchLabelSyntax caseLabel &&
            SymbolicReachabilityService.TryTranslateValue(
                caseLabel.Value,
                semanticModel,
                cancellationToken,
                out var caseValue,
                getSymbolVersion) &&
            caseValue != null &&
            SymbolicFactFactory.CanCompareSmtValues(governingValue, caseValue))
        {
            formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, governingValue, caseValue);
            return true;
        }

        if (label is CasePatternSwitchLabelSyntax patternLabel)
            return TryCreatePatternAndGuardSelectionCondition(
                governingValue,
                governingType,
                patternLabel.Pattern,
                patternLabel.WhenClause,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion);

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
        if (!TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken,
                out var governingValue, getSymbolVersion)) return false;

        var domainFacts = new List<SmtFormula>();
        SymbolicReachabilityService.TryCollectDomainFacts(
            governingExpression,
            semanticModel,
            cancellationToken,
            domainFacts,
            getSymbolVersion);

        if (label is CaseSwitchLabelSyntax caseLabel &&
            SymbolicReachabilityService.TryTranslateValue(
                caseLabel.Value,
                semanticModel,
                cancellationToken,
                out var caseValue,
                getSymbolVersion) &&
            caseValue != null &&
            SymbolicFactFactory.CanCompareSmtValues(governingValue, caseValue))
        {
            domainFacts.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, governingValue, caseValue));
            return TryCreateConjunction(domainFacts, out formula);
        }

        if (label is CasePatternSwitchLabelSyntax patternLabel)
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
        if (currentArm.Parent is not SwitchExpressionSyntax switchExpression) return true;

        var priorArmConditions = new List<SmtFormula>();
        foreach (var arm in switchExpression.Arms)
        {
            if (ReferenceEquals(arm, currentArm)) break;

            if (!TryCreatePatternAndGuardSelectionCondition(
                    governingValue,
                    governingType,
                    arm.Pattern,
                    arm.WhenClause,
                    semanticModel,
                    cancellationToken,
                    out var priorArmCondition,
                    getSymbolVersion))
                continue;

            priorArmConditions.Add(priorArmCondition);
        }

        if (priorArmConditions.Count == 0) return true;

        if (!TryCreateDisjunction(priorArmConditions, out var priorArmDisjunction)) return false;

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
        if (!TryCreatePatternSelectionCondition(
                governingValue,
                governingType,
                pattern,
                semanticModel,
                cancellationToken,
                out var patternFormula,
                getSymbolVersion) ||
            patternFormula == null)
            return false;

        var conditions = new List<SmtFormula> { patternFormula };
        var bindingFacts = new List<SmtFormula>();
        SymbolicReachabilityService.TryCollectPatternBindingFacts(
            governingValue,
            governingType,
            pattern,
            semanticModel,
            cancellationToken,
            bindingFacts,
            getSymbolVersion);

        if (whenClause != null)
        {
            if (!SymbolicReachabilityService.TryTranslateConditionFormula(
                    whenClause.Condition,
                    semanticModel,
                    cancellationToken,
                    out var guardFormula,
                    getSymbolVersion) ||
                guardFormula == null)
                return false;

            conditions.Add(SubstitutePatternBindingFacts(guardFormula, bindingFacts));
        }

        return TryCreateConjunction(conditions, out formula);
    }

    private static SmtFormula SubstitutePatternBindingFacts(
        SmtFormula formula,
        IEnumerable<SmtFormula> bindingFacts)
    {
        var substitutions = new Dictionary<SmtVariable, SmtFormula>();
        foreach (var bindingFact in bindingFacts)
        {
            if (bindingFact is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equality) continue;

            AddBindingSubstitution(equality.Left, equality.Right, substitutions);
            AddBindingSubstitution(equality.Right, equality.Left, substitutions);
        }

        return substitutions.Count == 0
            ? formula
            : SubstitutePatternBindingFacts(formula, substitutions);
    }

    private static void AddBindingSubstitution(
        SmtFormula source,
        SmtFormula replacement,
        IDictionary<SmtVariable, SmtFormula> substitutions)
    {
        if (source is not SmtVariable variable ||
            variable.Kind != replacement.Kind ||
            ReferencesFormula(replacement, variable))
            return;

        substitutions[variable] = replacement;
    }

    private static SmtFormula SubstitutePatternBindingFacts(
        SmtFormula formula,
        IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions)
    {
        switch (formula)
        {
            case SmtVariable variable when substitutions.TryGetValue(variable, out var replacement):
                return replacement;
            case SmtUnaryFormula unary:
                return new SmtUnaryFormula(
                    unary.Operator,
                    SubstitutePatternBindingFacts(unary.Operand, substitutions));
            case SmtBinaryFormula binary:
                return new SmtBinaryFormula(
                    binary.Operator,
                    SubstitutePatternBindingFacts(binary.Left, substitutions),
                    SubstitutePatternBindingFacts(binary.Right, substitutions));
            case SmtIntegerUnaryTerm integerUnary:
                return new SmtIntegerUnaryTerm(
                    integerUnary.Operator,
                    SubstitutePatternBindingFacts(integerUnary.Operand, substitutions));
            case SmtIntegerBinaryTerm integerBinary:
                return new SmtIntegerBinaryTerm(
                    integerBinary.Operator,
                    SubstitutePatternBindingFacts(integerBinary.Left, substitutions),
                    SubstitutePatternBindingFacts(integerBinary.Right, substitutions));
            case SmtStringLengthTerm stringLength:
                return new SmtStringLengthTerm(SubstitutePatternBindingFacts(stringLength.Value, substitutions));
            case SmtStringConcatTerm stringConcat:
                return new SmtStringConcatTerm(
                    SubstitutePatternBindingFacts(stringConcat.Left, substitutions),
                    SubstitutePatternBindingFacts(stringConcat.Right, substitutions));
            case SmtStringContainsFormula stringContains:
                return new SmtStringContainsFormula(
                    SubstitutePatternBindingFacts(stringContains.Value, substitutions),
                    SubstitutePatternBindingFacts(stringContains.Search, substitutions));
            case SmtStringStartsWithFormula stringStartsWith:
                return new SmtStringStartsWithFormula(
                    SubstitutePatternBindingFacts(stringStartsWith.Value, substitutions),
                    SubstitutePatternBindingFacts(stringStartsWith.Prefix, substitutions));
            case SmtStringEndsWithFormula stringEndsWith:
                return new SmtStringEndsWithFormula(
                    SubstitutePatternBindingFacts(stringEndsWith.Value, substitutions),
                    SubstitutePatternBindingFacts(stringEndsWith.Suffix, substitutions));
            case SmtRegexMatchFormula regexMatch:
                return new SmtRegexMatchFormula(
                    SubstitutePatternBindingFacts(regexMatch.Value, substitutions),
                    regexMatch.Pattern,
                    regexMatch.Options);
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return new SmtRuntimeTypeTestFormula(
                    SubstitutePatternBindingFacts(runtimeTypeTest.Value, substitutions),
                    runtimeTypeTest.TypeKey);
            case SmtConditionalFormula conditional:
                return new SmtConditionalFormula(
                    SubstitutePatternBindingFacts(conditional.Condition, substitutions),
                    SubstitutePatternBindingFacts(conditional.WhenTrue, substitutions),
                    SubstitutePatternBindingFacts(conditional.WhenFalse, substitutions),
                    conditional.ResultKind);
            default:
                return formula;
        }
    }

    private static bool ReferencesFormula(SmtFormula formula, SmtFormula target)
    {
        if (formula.Equals(target)) return true;

        switch (formula)
        {
            case SmtUnaryFormula unary:
                return ReferencesFormula(unary.Operand, target);
            case SmtBinaryFormula binary:
                return ReferencesFormula(binary.Left, target) ||
                       ReferencesFormula(binary.Right, target);
            case SmtIntegerUnaryTerm integerUnary:
                return ReferencesFormula(integerUnary.Operand, target);
            case SmtIntegerBinaryTerm integerBinary:
                return ReferencesFormula(integerBinary.Left, target) ||
                       ReferencesFormula(integerBinary.Right, target);
            case SmtStringLengthTerm stringLength:
                return ReferencesFormula(stringLength.Value, target);
            case SmtStringConcatTerm stringConcat:
                return ReferencesFormula(stringConcat.Left, target) ||
                       ReferencesFormula(stringConcat.Right, target);
            case SmtStringContainsFormula stringContains:
                return ReferencesFormula(stringContains.Value, target) ||
                       ReferencesFormula(stringContains.Search, target);
            case SmtStringStartsWithFormula stringStartsWith:
                return ReferencesFormula(stringStartsWith.Value, target) ||
                       ReferencesFormula(stringStartsWith.Prefix, target);
            case SmtStringEndsWithFormula stringEndsWith:
                return ReferencesFormula(stringEndsWith.Value, target) ||
                       ReferencesFormula(stringEndsWith.Suffix, target);
            case SmtRegexMatchFormula regexMatch:
                return ReferencesFormula(regexMatch.Value, target);
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return ReferencesFormula(runtimeTypeTest.Value, target);
            case SmtConditionalFormula conditional:
                return ReferencesFormula(conditional.Condition, target) ||
                       ReferencesFormula(conditional.WhenTrue, target) ||
                       ReferencesFormula(conditional.WhenFalse, target);
            default:
                return false;
        }
    }

    private static bool TryCreatePatternSelectionCondition(
        SmtFormula value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion)
    {
        formula = null;

        if (CanUseTranslatedPatternForSelection(pattern, valueType, semanticModel, cancellationToken) &&
            SymbolicReachabilityService.TryTranslatePattern(
                value,
                pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                valueType) &&
            formula != null)
            return true;

        if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            return TryCreatePatternSelectionCondition(
                value,
                valueType,
                parenthesizedPattern.Pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion);

        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
            TryCreatePatternSelectionCondition(
                value,
                valueType,
                unaryPattern.Pattern,
                semanticModel,
                cancellationToken,
                out var negatedPattern,
                getSymbolVersion) &&
            negatedPattern != null)
        {
            formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
            return true;
        }

        if (pattern is BinaryPatternSyntax binaryPattern)
            return TryCreateBinaryPatternSelectionCondition(
                value,
                valueType,
                binaryPattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion);

        if (pattern is RecursivePatternSyntax recursivePattern)
            return TryCreateRecursivePatternSelectionCondition(
                value,
                valueType,
                recursivePattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion);

        if (pattern is ListPatternSyntax listPattern)
            return TryCreateListPatternSelectionCondition(
                value,
                valueType,
                listPattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion);

        return false;
    }

    private static bool TryCreateBinaryPatternSelectionCondition(
        SmtFormula value,
        ITypeSymbol? valueType,
        BinaryPatternSyntax binaryPattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion)
    {
        formula = null;
        if (!TryCreatePatternSelectionCondition(
                value,
                valueType,
                binaryPattern.Left,
                semanticModel,
                cancellationToken,
                out var leftPattern,
                getSymbolVersion) ||
            !TryCreatePatternSelectionCondition(
                value,
                valueType,
                binaryPattern.Right,
                semanticModel,
                cancellationToken,
                out var rightPattern,
                getSymbolVersion) ||
            leftPattern == null ||
            rightPattern == null)
            return false;

        if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
        {
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftPattern, rightPattern);
            return true;
        }

        if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
        {
            formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftPattern, rightPattern);
            return true;
        }

        return false;
    }

    private static bool TryCreateRecursivePatternSelectionCondition(
        SmtFormula value,
        ITypeSymbol? valueType,
        RecursivePatternSyntax recursivePattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion)
    {
        formula = null;
        if (!CanUseRecursivePatternTypeAsStructuralFact(
                recursivePattern,
                valueType,
                semanticModel,
                cancellationToken))
            return false;

        var conditions = new List<SmtFormula>();
        if (value.Kind == SmtValueKind.Reference &&
            (valueType == null || valueType.IsReferenceType))
            AddReferenceNonNullFact(value, conditions);

        var propertySubpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
        if (propertySubpatterns != null)
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
                    memberType == null ||
                    !TryCreatePatternSelectionCondition(
                        memberValue,
                        memberType,
                        subpattern.Pattern,
                        semanticModel,
                        cancellationToken,
                        out var subpatternCondition,
                        getSymbolVersion) ||
                    subpatternCondition == null)
                    return false;

                if (pathCondition != null) conditions.Add(pathCondition);

                conditions.Add(subpatternCondition);
            }

        var positionalSubpatterns = recursivePattern.PositionalPatternClause?.Subpatterns;
        if (positionalSubpatterns != null)
            for (var position = 0; position < positionalSubpatterns.Value.Count; position++)
            {
                if (!TryResolveTuplePositionalSubpatternValue(
                        value,
                        valueType,
                        position,
                        out var memberValue,
                        out var memberType) ||
                    memberValue == null ||
                    memberType == null ||
                    !TryCreatePatternSelectionCondition(
                        memberValue,
                        memberType,
                        positionalSubpatterns.Value[position].Pattern,
                        semanticModel,
                        cancellationToken,
                        out var subpatternCondition,
                        getSymbolVersion) ||
                    subpatternCondition == null)
                    return false;

                conditions.Add(subpatternCondition);
            }

        if (conditions.Count == 0)
        {
            formula = new SmtBooleanConstant(true);
            return true;
        }

        return TryCreateConjunction(conditions, out formula);
    }

    private static bool TryCreateListPatternSelectionCondition(
        SmtFormula value,
        ITypeSymbol? valueType,
        ListPatternSyntax listPattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion)
    {
        formula = null;
        if (value.Kind != SmtValueKind.Reference ||
            !TryCreateListPatternLengthFormula(value, valueType, semanticModel, out var lengthFormula))
            return false;

        var conditions = new List<SmtFormula>();
        AddReferenceNonNullFact(value, conditions);

        conditions.Add(CreateListPatternLengthCondition(listPattern, lengthFormula));

        if (!TryAddListPatternElementSelectionConditions(
                value,
                valueType,
                listPattern,
                semanticModel,
                cancellationToken,
                conditions,
                getSymbolVersion))
            return false;

        return TryCreateConjunction(conditions, out formula);
    }

    private static bool TryAddListPatternElementSelectionConditions(
        SmtFormula value,
        ITypeSymbol? valueType,
        ListPatternSyntax listPattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> conditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (!TryGetListPatternElementType(valueType, out var elementType) ||
            !TryGetValueKind(elementType, out var elementKind))
            return ListPatternHasOnlySelectionNeutralElements(listPattern);

        for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
        {
            var subpattern = listPattern.Patterns[patternIndex];
            if (subpattern is SlicePatternSyntax slicePattern)
            {
                if (!TryAddListPatternSliceSelectionConditions(
                        value,
                        valueType,
                        listPattern,
                        patternIndex,
                        slicePattern,
                        semanticModel,
                        cancellationToken,
                        conditions,
                        getSymbolVersion))
                    return false;

                continue;
            }

            if (IsSelectionNeutralPattern(subpattern)) continue;

            if (!CSharpSyntaxFacts.TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex,
                    out var fromEnd)) return false;

            var elementValue = CreateListPatternElementFormula(value, elementIndex, fromEnd, elementKind);
            if (!TryCreatePatternSelectionCondition(
                    elementValue,
                    elementType,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    out var elementCondition,
                    getSymbolVersion) ||
                elementCondition == null)
                return false;

            conditions.Add(elementCondition);
        }

        return true;
    }

    private static bool TryAddListPatternSliceSelectionConditions(
        SmtFormula value,
        ITypeSymbol? valueType,
        ListPatternSyntax containingPattern,
        int sliceIndex,
        SlicePatternSyntax slicePattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> conditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (IsSelectionNeutralSlicePattern(slicePattern.Pattern)) return true;

        if (!CSharpSyntaxFacts.TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern)) return false;

        if (!TryGetListPatternElementType(valueType, out var elementType) ||
            !TryGetValueKind(elementType, out var elementKind))
            return ListPatternHasOnlySelectionNeutralElements(nestedListPattern);

        return TryAddProjectedListPatternSelectionConditions(
            value,
            elementType,
            elementKind,
            nestedListPattern,
            GetListPatternPrefixElementCount(containingPattern, sliceIndex),
            GetListPatternSuffixElementCount(containingPattern, sliceIndex),
            semanticModel,
            cancellationToken,
            conditions,
            getSymbolVersion);
    }

    private static bool TryAddProjectedListPatternSelectionConditions(
        SmtFormula value,
        ITypeSymbol elementType,
        SmtValueKind elementKind,
        ListPatternSyntax listPattern,
        int prefixOffset,
        int suffixOffset,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> conditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
        {
            var subpattern = listPattern.Patterns[patternIndex];
            if (subpattern is SlicePatternSyntax slicePattern)
            {
                if (IsSelectionNeutralSlicePattern(slicePattern.Pattern)) continue;

                if (!CSharpSyntaxFacts.TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    return false;

                if (!TryAddProjectedListPatternSelectionConditions(
                        value,
                        elementType,
                        elementKind,
                        nestedListPattern,
                        prefixOffset + GetListPatternPrefixElementCount(listPattern, patternIndex),
                        suffixOffset + GetListPatternSuffixElementCount(listPattern, patternIndex),
                        semanticModel,
                        cancellationToken,
                        conditions,
                        getSymbolVersion))
                    return false;

                continue;
            }

            if (IsSelectionNeutralPattern(subpattern)) continue;

            if (!TryGetProjectedListPatternElementPosition(
                    listPattern,
                    patternIndex,
                    prefixOffset,
                    suffixOffset,
                    out var elementIndex,
                    out var fromEnd))
                return false;

            var elementValue = CreateListPatternElementFormula(value, elementIndex, fromEnd, elementKind);
            if (!TryCreatePatternSelectionCondition(
                    elementValue,
                    elementType,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    out var elementCondition,
                    getSymbolVersion) ||
                elementCondition == null)
                return false;

            conditions.Add(elementCondition);
        }

        return true;
    }

    private static bool CanUseTranslatedPatternForSelection(
        PatternSyntax pattern,
        ITypeSymbol? valueType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        pattern = UnwrapParenthesizedPattern(pattern);
        if (pattern is RecursivePatternSyntax recursivePattern)
            return CanUseRecursivePatternTypeAsStructuralFact(
                recursivePattern,
                valueType,
                semanticModel,
                cancellationToken);

        if (pattern is BinaryPatternSyntax binaryPattern)
            return CanUseTranslatedPatternForSelection(binaryPattern.Left, valueType, semanticModel,
                       cancellationToken) &&
                   CanUseTranslatedPatternForSelection(binaryPattern.Right, valueType, semanticModel,
                       cancellationToken);

        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword))
            return CanUseTranslatedPatternForSelection(unaryPattern.Pattern, valueType, semanticModel,
                cancellationToken);

        return true;
    }

    private static bool CanUseRecursivePatternTypeAsStructuralFact(
        RecursivePatternSyntax recursivePattern,
        ITypeSymbol? valueType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (recursivePattern.Type == null) return true;

        var patternType = semanticModel.GetTypeInfo(recursivePattern.Type, cancellationToken).Type;
        if (valueType == null ||
            patternType == null)
            return false;

        return semanticModel.Compilation.ClassifyConversion(valueType, patternType).IsImplicit;
    }

    private static PatternSyntax UnwrapParenthesizedPattern(PatternSyntax pattern)
    {
        while (pattern is ParenthesizedPatternSyntax parenthesizedPattern) pattern = parenthesizedPattern.Pattern;

        return pattern;
    }

    private static bool IsSelectionNeutralSlicePattern(PatternSyntax? pattern)
    {
        return pattern == null ||
               IsSelectionNeutralPattern(pattern);
    }

    private static bool IsSelectionNeutralPattern(PatternSyntax pattern)
    {
        pattern = UnwrapParenthesizedPattern(pattern);
        return pattern is DiscardPatternSyntax or VarPatternSyntax;
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
        if (SymbolicReachabilityService.TryTranslatePattern(
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
                false);
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
                false);
        }

        if (includePatternBindings)
            SymbolicReachabilityService.TryCollectPatternBindingFacts(
                governingValue,
                governingType,
                pattern,
                semanticModel,
                cancellationToken,
                conditions,
                getSymbolVersion);

        if (whenClause != null)
        {
            var bindingFacts = new List<SmtFormula>();
            SymbolicReachabilityService.TryCollectPatternBindingFacts(
                governingValue,
                governingType,
                pattern,
                semanticModel,
                cancellationToken,
                bindingFacts,
                getSymbolVersion);

            SymbolicReachabilityService.TryCollectDomainFacts(
                whenClause.Condition,
                semanticModel,
                cancellationToken,
                conditions,
                getSymbolVersion);

            var branchAssumptions = new List<SmtFormula>();
            if (SymbolicReachabilityService.TryCollectBranchAssumptions(
                    whenClause.Condition,
                    true,
                    semanticModel,
                    cancellationToken,
                    branchAssumptions,
                    getSymbolVersion))
                foreach (var branchAssumption in branchAssumptions)
                    conditions.Add(SubstitutePatternBindingFacts(branchAssumption, bindingFacts));

            if (SymbolicReachabilityService.TryTranslateConditionFormula(
                    whenClause.Condition,
                    semanticModel,
                    cancellationToken,
                    out var guardFormula,
                    getSymbolVersion) &&
                guardFormula != null)
                conditions.Add(guardFormula);
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
            SymbolicReachabilityService.TryTranslatePattern(
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
            AddRecursivePatternStructuralFacts(
                value,
                valueType,
                recursivePattern,
                semanticModel,
                cancellationToken,
                conditions,
                getSymbolVersion);
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
            AddReferenceNonNullFact(value, conditions);

        var propertySubpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
        if (propertySubpatterns != null)
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
                    continue;

                if (pathCondition != null) conditions.Add(pathCondition);

                AddStructuralPatternFacts(
                    memberValue,
                    memberType,
                    subpattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);
            }

        var positionalSubpatterns = recursivePattern.PositionalPatternClause?.Subpatterns;
        if (positionalSubpatterns == null) return;

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
                continue;

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
            return;

        AddReferenceNonNullFact(value, conditions);

        conditions.Add(CreateListPatternLengthCondition(listPattern, lengthFormula));

        if (!TryGetListPatternElementType(valueType, out var elementType) ||
            !TryGetValueKind(elementType, out var elementKind))
            return;

        for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
        {
            var subpattern = listPattern.Patterns[patternIndex];
            if (subpattern is SlicePatternSyntax slicePattern)
            {
                AddListPatternSliceStructuralFacts(
                    value,
                    elementType,
                    elementKind,
                    listPattern,
                    patternIndex,
                    slicePattern,
                    semanticModel,
                    cancellationToken,
                    conditions,
                    getSymbolVersion);

                continue;
            }

            if (!CSharpSyntaxFacts.TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex,
                    out var fromEnd)) continue;

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

    private static void AddListPatternSliceStructuralFacts(
        SmtFormula value,
        ITypeSymbol elementType,
        SmtValueKind elementKind,
        ListPatternSyntax containingPattern,
        int sliceIndex,
        SlicePatternSyntax slicePattern,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> conditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (!CSharpSyntaxFacts.TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern)) return;

        AddProjectedListPatternStructuralFacts(
            value,
            elementType,
            elementKind,
            nestedListPattern,
            GetListPatternPrefixElementCount(containingPattern, sliceIndex),
            GetListPatternSuffixElementCount(containingPattern, sliceIndex),
            semanticModel,
            cancellationToken,
            conditions,
            getSymbolVersion);
    }

    private static void AddProjectedListPatternStructuralFacts(
        SmtFormula value,
        ITypeSymbol elementType,
        SmtValueKind elementKind,
        ListPatternSyntax listPattern,
        int prefixOffset,
        int suffixOffset,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> conditions,
        Func<ISymbol, int>? getSymbolVersion)
    {
        for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
        {
            var subpattern = listPattern.Patterns[patternIndex];
            if (subpattern is SlicePatternSyntax slicePattern)
            {
                if (CSharpSyntaxFacts.TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    AddProjectedListPatternStructuralFacts(
                        value,
                        elementType,
                        elementKind,
                        nestedListPattern,
                        prefixOffset + GetListPatternPrefixElementCount(listPattern, patternIndex),
                        suffixOffset + GetListPatternSuffixElementCount(listPattern, patternIndex),
                        semanticModel,
                        cancellationToken,
                        conditions,
                        getSymbolVersion);

                continue;
            }

            if (!TryGetProjectedListPatternElementPosition(
                    listPattern,
                    patternIndex,
                    prefixOffset,
                    suffixOffset,
                    out var elementIndex,
                    out var fromEnd))
                continue;

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
        if (valueType?.SpecialType == SpecialType.System_String)
            return TryCreateStringLengthFormula(value, out lengthFormula);

        var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        if (!TryGetListPatternLengthMemberName(valueType, out var memberName) ||
            !TryCreateMemberFormula(value, memberName, intType, out var memberFormula) ||
            memberFormula == null)
            return false;

        lengthFormula = memberFormula;
        return true;
    }

    private static bool TryGetListPatternLengthMemberName(ITypeSymbol? valueType, out string memberName)
    {
        if (valueType is IArrayTypeSymbol { Rank: 1 })
        {
            memberName = "Length";
            return true;
        }

        if (SymbolicTypeFacts.HasInstanceInt32Member(valueType, "Length"))
        {
            memberName = "Length";
            return true;
        }

        if (SymbolicTypeFacts.HasInstanceInt32Member(valueType, "Count"))
        {
            memberName = "Count";
            return true;
        }

        memberName = string.Empty;
        return false;
    }

    private static bool TryGetListPatternElementType(ITypeSymbol? valueType, out ITypeSymbol elementType)
    {
        if (valueType is IArrayTypeSymbol { Rank: 1 } arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (TryGetSingleIntIndexerElementType(valueType, out elementType)) return true;

        elementType = null!;
        return false;
    }

    private static bool TryGetSingleIntIndexerElementType(ITypeSymbol? valueType, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (valueType == null) return false;

        for (var current = valueType; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            if (TryGetSingleDeclaredIntIndexerElementType(current, out elementType))
                return true;

        foreach (var interfaceType in valueType.AllInterfaces)
            if (TryGetSingleDeclaredIntIndexerElementType(interfaceType, out elementType))
                return true;

        elementType = null!;
        return false;
    }

    private static bool TryGetSingleDeclaredIntIndexerElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        ITypeSymbol? candidateType = null;
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (!IsSupportedListPatternIndexer(property)) continue;

            if (candidateType != null &&
                !SymbolEqualityComparer.Default.Equals(candidateType, property.Type))
            {
                elementType = null!;
                return false;
            }

            candidateType = property.Type;
        }

        if (candidateType == null)
        {
            elementType = null!;
            return false;
        }

        elementType = candidateType;
        return true;
    }

    private static bool IsSupportedListPatternIndexer(IPropertySymbol property)
    {
        return property.IsIndexer &&
               !property.IsStatic &&
               property.Parameters.Length == 1 &&
               property.Parameters[0].Type.SpecialType == SpecialType.System_Int32;
    }

    private static SmtFormula CreateListPatternLengthCondition(
        ListPatternSyntax listPattern,
        SmtFormula lengthFormula)
    {
        CSharpSyntaxFacts.GetListPatternLengthShape(listPattern, out var minimumLength, out var exactLength);
        return exactLength
            ? new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                lengthFormula,
                new SmtIntegerConstant(minimumLength))
            : new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                lengthFormula,
                new SmtIntegerConstant(minimumLength));
    }

    private static bool TryGetProjectedListPatternElementPosition(
        ListPatternSyntax listPattern,
        int patternIndex,
        int prefixOffset,
        int suffixOffset,
        out int elementIndex,
        out bool fromEnd)
    {
        if (!CSharpSyntaxFacts.TryGetListPatternElementPosition(listPattern, patternIndex, out elementIndex,
                out fromEnd)) return false;

        if (fromEnd)
            elementIndex += suffixOffset;
        else
            elementIndex += prefixOffset;

        return true;
    }

    private static int GetListPatternPrefixElementCount(
        ListPatternSyntax listPattern,
        int sliceIndex)
    {
        var count = 0;
        for (var index = 0; index < sliceIndex; index++)
            if (listPattern.Patterns[index] is not SlicePatternSyntax)
                count++;

        return count;
    }

    private static int GetListPatternSuffixElementCount(
        ListPatternSyntax listPattern,
        int sliceIndex)
    {
        var count = 0;
        for (var index = sliceIndex + 1; index < listPattern.Patterns.Count; index++)
            if (listPattern.Patterns[index] is not SlicePatternSyntax)
                count++;

        return count;
    }

    private static bool ListPatternHasOnlySelectionNeutralElements(ListPatternSyntax listPattern)
    {
        foreach (var subpattern in listPattern.Patterns)
        {
            if (subpattern is SlicePatternSyntax slicePattern)
            {
                if (IsSelectionNeutralSlicePattern(slicePattern.Pattern)) continue;

                if (!CSharpSyntaxFacts.TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern) ||
                    !ListPatternHasOnlySelectionNeutralElements(nestedListPattern))
                    return false;

                continue;
            }

            if (!IsSelectionNeutralPattern(subpattern)) return false;
        }

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

    private static bool TryResolveTuplePositionalSubpatternValue(
        SmtFormula receiver,
        ITypeSymbol? receiverType,
        int position,
        out SmtFormula? memberValue,
        out ITypeSymbol? memberType)
    {
        memberValue = null;
        memberType = null;
        if (!SymbolicTypeFacts.TryGetTuplePositionalField(receiverType, position, out var fieldSymbol) ||
            !TryGetTupleElementStorageName(fieldSymbol, out var storageName) ||
            !TryGetValueKind(fieldSymbol.Type, out var kind))
            return false;

        memberType = fieldSymbol.Type;
        memberValue = new SmtVariable(GetFormulaVariableName(receiver) + "." + storageName, kind);
        return true;
    }

    private static bool TryGetTupleElementStorageName(IFieldSymbol fieldSymbol, out string storageName)
    {
        var tupleField = fieldSymbol.CorrespondingTupleField ?? fieldSymbol;
        if (SymbolicTypeFacts.IsTupleElementStorageName(tupleField.Name))
        {
            storageName = tupleField.Name;
            return true;
        }

        storageName = string.Empty;
        return false;
    }

    private static void AddReferenceNonNullFact(
        SmtFormula value,
        ICollection<SmtFormula> conditions)
    {
        if (value.Kind != SmtValueKind.Reference) return;

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
            return false;

        var currentValue = receiver;
        for (var index = 0; index < memberNames.Count; index++)
        {
            var memberName = memberNames[index];
            var memberSymbol = semanticModel.GetSymbolInfo(memberName, cancellationToken).Symbol;
            if (!SymbolicTypeFacts.TryGetMemberType(memberSymbol, out memberType)) return false;

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
        if (receiver.Kind != SmtValueKind.Reference) return false;

        var receiverName = receiver is SmtVariable variable
            ? variable.Name
            : receiver.ToString();
        if (string.IsNullOrEmpty(receiverName)) return false;

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
            SymbolicTypeFacts.IsSupportedTupleCarrierType(type))
        {
            kind = SmtValueKind.Reference;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType is
                   SpecialType.System_Char or
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
        if (formulas.Count == 0) return false;

        formula = formulas[0];
        for (var index = 1; index < formulas.Count; index++)
            formula = new SmtBinaryFormula(smtOperator, formula, formulas[index]);

        return true;
    }
}