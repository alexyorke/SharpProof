namespace SharpProof.Symbolic.Smt;
internal static class SwitchPathConditionBuilder {
    internal static bool TryCreateSwitchStatementSectionSymbolicCondition(
        ExpressionSyntax governingExpression,
        SwitchSectionSyntax section,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition condition,
        Func<ISymbol, int>? getSymbolVersion = null) => TryCreateCanonicalSwitchStatementSectionCondition(
            governingExpression,
            section,
            semanticModel,
            cancellationToken,
            out condition,
            getSymbolVersion);
    internal static bool TryCreateSwitchStatementSectionSymbolicCondition(
        ExpressionSyntax governingExpression,
        SwitchSectionSyntax section,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) => TryCreateCanonicalSwitchStatementSectionCondition(
            governingExpression,
            section,
            context.SemanticModel,
            context.CancellationToken,
            out condition,
            context.GetSymbolVersion,
            context);
    internal static bool TryCreateSwitchStatementLabelSymbolicCondition(
        ExpressionSyntax governingExpression,
        SwitchLabelSyntax label,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition condition,
        Func<ISymbol, int>? getSymbolVersion = null) {
        condition = null!;
        return TryLowerCanonicalSwitchGoverningValue(
                   governingExpression,
                   semanticModel,
                   cancellationToken,
                   getSymbolVersion,
                   out var governingValue,
                   out var governingType,
                   out var context) &&
               TryCreateCanonicalSwitchLabelCondition(governingValue, governingType, label, context, out condition);
    }
    internal static bool TryCreateSwitchExpressionArmSymbolicCondition(
        ExpressionSyntax governingExpression,
        SwitchExpressionArmSyntax arm,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition condition,
        Func<ISymbol, int>? getSymbolVersion = null) => TryCreateCanonicalSwitchExpressionArmCondition(
            governingExpression,
            arm,
            semanticModel,
            cancellationToken,
            out condition,
            getSymbolVersion);
    private static bool TryCreateCanonicalSwitchStatementSectionCondition(
        ExpressionSyntax governingExpression,
        SwitchSectionSyntax section,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition condition,
        Func<ISymbol, int>? getSymbolVersion,
        SymbolicLoweringContext? existingContext = null) {
        condition = null!;
        if (section.Parent is not SwitchStatementSyntax switchStatement ||
            !TryLowerCanonicalSwitchGoverningValue(
                governingExpression,
                semanticModel,
                cancellationToken,
                getSymbolVersion,
                out var governingValue,
                out var governingType,
                out var context,
                existingContext))
            return false;
        if (section.Labels.Any(static label => label is DefaultSwitchLabelSyntax)) {
            var explicitSelections = new List<SymbolicCondition>();
            foreach (var candidateSection in switchStatement.Sections)
                foreach (var label in candidateSection.Labels)
                    if (label is not DefaultSwitchLabelSyntax &&
                        TryCreateCanonicalSwitchLabelCondition(governingValue, governingType, label, context, out var labelCondition))
                        explicitSelections.Add(labelCondition);
            condition = explicitSelections.Count == 0
                ? new SymbolicConstantCondition(true)
                : new SymbolicNotCondition(CreateCanonicalDisjunction(explicitSelections));
            return true;
        }
        var currentSelections = new List<SymbolicCondition>();
        foreach (var label in section.Labels)
            if (TryCreateCanonicalSwitchLabelCondition(governingValue, governingType, label, context, out var labelCondition))
                currentSelections.Add(labelCondition);
            else
                return false;
        if (currentSelections.Count == 0) return false;
        var selected = CreateCanonicalDisjunction(currentSelections);
        var priorSelections = new List<SymbolicCondition>();
        foreach (var candidateSection in switchStatement.Sections) {
            if (ReferenceEquals(candidateSection, section)) break;
            foreach (var label in candidateSection.Labels)
                if (label is not DefaultSwitchLabelSyntax &&
                    TryCreateCanonicalSwitchLabelCondition(governingValue, governingType, label, context, out var priorCondition))
                    priorSelections.Add(priorCondition);
        }
        condition = priorSelections.Count == 0
            ? selected
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicNotCondition(CreateCanonicalDisjunction(priorSelections)),
                selected);
        return true;
    }
    private static bool TryCreateCanonicalSwitchExpressionArmCondition(
        ExpressionSyntax governingExpression,
        SwitchExpressionArmSyntax arm,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition condition,
        Func<ISymbol, int>? getSymbolVersion) {
        condition = null!;
        if (arm.Parent is not SwitchExpressionSyntax switchExpression ||
            !TryLowerCanonicalSwitchGoverningValue(
                governingExpression,
                semanticModel,
                cancellationToken,
                getSymbolVersion,
                out var governingValue,
                out var governingType,
                out var context) ||
            !TryCreateCanonicalPatternAndGuardCondition(
                governingValue,
                governingType,
                arm.Pattern,
                arm.WhenClause,
                context,
                out var currentCondition))
            return false;
        var priorSelections = new List<SymbolicCondition>();
        foreach (var candidate in switchExpression.Arms) {
            if (ReferenceEquals(candidate, arm)) break;
            if (TryCreateCanonicalPatternAndGuardCondition(
                    governingValue,
                    governingType,
                    candidate.Pattern,
                    candidate.WhenClause,
                    context,
                    out var priorCondition))
                priorSelections.Add(priorCondition);
        }
        condition = priorSelections.Count == 0
            ? currentCondition
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicNotCondition(CreateCanonicalDisjunction(priorSelections)),
                currentCondition);
        return true;
    }
    private static bool TryLowerCanonicalSwitchGoverningValue(
        ExpressionSyntax governingExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion,
        out SymbolicTerm governingValue,
        out ITypeSymbol? governingType,
        out SymbolicLoweringContext context,
        SymbolicLoweringContext? existingContext = null) {
        context = existingContext ?? new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
        governingValue = null!;
        var typeInfo = semanticModel.GetTypeInfo(governingExpression, cancellationToken);
        governingType = typeInfo.ConvertedType ?? typeInfo.Type;
        var lowering = SymbolicSemanticPipeline.LowerTerm(governingExpression, context);
        if (lowering is not { IsExact: true, Value: { } value }) return false;
        governingValue = value;
        return true;
    }
    private static bool TryCreateCanonicalSwitchLabelCondition(
        SymbolicTerm governingValue,
        ITypeSymbol? governingType,
        SwitchLabelSyntax label,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (label is CaseSwitchLabelSyntax constantLabel &&
            SymbolicSemanticPipeline.LowerTerm(constantLabel.Value, context) is { IsExact: true, Value: { } constant } &&
            CanCompareCanonicalTerms(governingValue, constant)) {
            condition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(SymbolicRelationOperator.Equal, governingValue, constant),
                constantLabel,
                "ir.switch.constant"));
            return true;
        }
        return label is CasePatternSwitchLabelSyntax patternLabel &&
               TryCreateCanonicalPatternAndGuardCondition(
                   governingValue,
                   governingType,
                   patternLabel.Pattern,
                   patternLabel.WhenClause,
                   context,
                   out condition);
    }
    private static bool TryCreateCanonicalPatternAndGuardCondition(
        SymbolicTerm governingValue,
        ITypeSymbol? governingType,
        PatternSyntax pattern,
        WhenClauseSyntax? whenClause,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        var patternLowering = SymbolicSemanticPipeline.LowerPatternCondition(governingValue, governingType, pattern, pattern, context);
        if (patternLowering is not { IsExact: true, Value: { } loweredCondition }) {
            condition = null!;
            return false;
        }
        condition = loweredCondition;
        var designationNames = new HashSet<string>(pattern.DescendantNodesAndSelf()
            .OfType<SingleVariableDesignationSyntax>()
            .Select(designation => context.SemanticModel.GetDeclaredSymbol(designation, context.CancellationToken))
            .OfType<ILocalSymbol>()
            .Select(context.GetVariableName), StringComparer.Ordinal);
        var bindings = new Dictionary<string, SymbolicTerm>(StringComparer.Ordinal);
        CollectCanonicalDesignationBindings(condition, designationNames, bindings);
        condition = RemoveCanonicalDesignationBindings(condition, designationNames);
        if (whenClause?.Condition is { } guard) {
            var lowering = SymbolicSemanticPipeline.LowerCondition(guard, context);
            if (lowering is not { IsExact: true, Value: { } guardCondition }) return false;
            guardCondition = SubstituteCanonicalTerms(guardCondition, bindings);
            condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, condition, guardCondition);
        }
        return true;
    }
    private static void CollectCanonicalDesignationBindings(
        SymbolicCondition condition,
        ISet<string> designationNames,
        IDictionary<string, SymbolicTerm> bindings) {
        switch (condition) {
            case SymbolicFactCondition {
                Fact: {
                    Polarity: true,
                    Atom: SymbolicRelationAtom {
                        Operator: SymbolicRelationOperator.Equal,
                        Left: var left,
                        Right: var right
                    }
                }
            }:
                if (left is SymbolicVariableTerm leftVariable && designationNames.Contains(leftVariable.Name))
                    bindings[leftVariable.Name] = right;
                else if (right is SymbolicVariableTerm rightVariable && designationNames.Contains(rightVariable.Name))
                    bindings[rightVariable.Name] = left;
                break;
            case SymbolicNotCondition not:
                CollectCanonicalDesignationBindings(not.Operand, designationNames, bindings);
                break;
            case SymbolicBinaryCondition binary:
                CollectCanonicalDesignationBindings(binary.Left, designationNames, bindings);
                CollectCanonicalDesignationBindings(binary.Right, designationNames, bindings);
                break;
        }
    }
    private static SymbolicCondition RemoveCanonicalDesignationBindings(SymbolicCondition condition, ISet<string> designationNames)
        => condition switch {
            SymbolicFactCondition {
                Fact: {
                    Polarity: true,
                    Atom: SymbolicRelationAtom {
                        Operator: SymbolicRelationOperator.Equal,
                        Left: var left,
                        Right: var right
                    }
                }
            } when IsCanonicalDesignationTerm(left, designationNames) ||
                   IsCanonicalDesignationTerm(right, designationNames) => new SymbolicConstantCondition(true),
            SymbolicNotCondition not =>
                new SymbolicNotCondition(RemoveCanonicalDesignationBindings(not.Operand, designationNames)),
            SymbolicBinaryCondition binary => new SymbolicBinaryCondition(
                binary.Operator,
                RemoveCanonicalDesignationBindings(binary.Left, designationNames),
                RemoveCanonicalDesignationBindings(binary.Right, designationNames)),
            _ => condition
        };
    private static bool IsCanonicalDesignationTerm(SymbolicTerm term, ISet<string> designationNames) =>
        term is SymbolicVariableTerm variable && designationNames.Contains(variable.Name);
    private static SymbolicCondition SubstituteCanonicalTerms(
        SymbolicCondition condition,
        IReadOnlyDictionary<string, SymbolicTerm> bindings) {
        if (bindings.Count == 0) return condition;
        return condition switch {
            SymbolicConstantCondition => condition,
            SymbolicFactCondition factCondition => new SymbolicFactCondition(
                factCondition.Fact with { Atom = SubstituteCanonicalTerms(factCondition.Fact.Atom, bindings) }),
            SymbolicNotCondition not => new SymbolicNotCondition(SubstituteCanonicalTerms(not.Operand, bindings)),
            SymbolicBinaryCondition binary => new SymbolicBinaryCondition(
                binary.Operator,
                SubstituteCanonicalTerms(binary.Left, bindings),
                SubstituteCanonicalTerms(binary.Right, bindings)),
            _ => condition
        };
    }
    private static SymbolicAtom SubstituteCanonicalTerms(SymbolicAtom atom, IReadOnlyDictionary<string, SymbolicTerm> bindings)
        => atom switch {
            SymbolicTruthAtom truth => new SymbolicTruthAtom(SubstituteCanonicalTerms(truth.Condition, bindings)),
            SymbolicRelationAtom relation => new SymbolicRelationAtom(
                relation.Operator,
                SubstituteCanonicalTerms(relation.Left, bindings),
                SubstituteCanonicalTerms(relation.Right, bindings)),
            SymbolicStringPredicateAtom predicate => new SymbolicStringPredicateAtom(
                predicate.Predicate,
                SubstituteCanonicalTerms(predicate.Value, bindings),
                SubstituteCanonicalTerms(predicate.Argument, bindings),
                predicate.RegexOptions),
            SymbolicBoundsAtom bounds => new SymbolicBoundsAtom(
                SubstituteCanonicalTerms(bounds.Index, bindings),
                SubstituteCanonicalTerms(bounds.Length, bindings),
                bounds.IncludeLowerBound,
                bounds.IncludeUpperBound),
            SymbolicExactRuntimeTypeAtom exactRuntimeType => new SymbolicExactRuntimeTypeAtom(
                SubstituteCanonicalTerms(exactRuntimeType.Value, bindings),
                exactRuntimeType.TypeKey),
            SymbolicTypeTestAtom typeTest => new SymbolicTypeTestAtom(SubstituteCanonicalTerms(typeTest.Value, bindings), typeTest.TypeKey),
            _ => atom
        };
    private static SymbolicTerm SubstituteCanonicalTerms(SymbolicTerm term, IReadOnlyDictionary<string, SymbolicTerm> bindings) {
        if (term is SymbolicVariableTerm variable && bindings.TryGetValue(variable.Name, out var replacement))
            return replacement;
        return term switch {
            SymbolicMemberTerm member => new SymbolicMemberTerm(
                SubstituteCanonicalTerms(member.Receiver, bindings),
                member.MemberName,
                member.Kind),
            SymbolicElementTerm element => new SymbolicElementTerm(
                SubstituteCanonicalTerms(element.Receiver, bindings),
                SubstituteCanonicalTerms(element.Index, bindings),
                element.Kind),
            SymbolicMultiElementTerm element => new SymbolicMultiElementTerm(
                SubstituteCanonicalTerms(element.Receiver, bindings),
                [.. element.Indices.Select(index => SubstituteCanonicalTerms(index, bindings))],
                element.Kind),
            SymbolicFromEndIndexTerm index =>
                new SymbolicFromEndIndexTerm(SubstituteCanonicalTerms(index.Value, bindings)),
            SymbolicStringContentTerm content =>
                new SymbolicStringContentTerm(SubstituteCanonicalTerms(content.Reference, bindings)),
            SymbolicStringConcatTerm concat => new SymbolicStringConcatTerm(
                SubstituteCanonicalTerms(concat.Left, bindings),
                SubstituteCanonicalTerms(concat.Right, bindings)),
            SymbolicLengthTerm length => new SymbolicLengthTerm(SubstituteCanonicalTerms(length.Value, bindings)),
            SymbolicArrayDimensionLengthTerm length => new SymbolicArrayDimensionLengthTerm(
                SubstituteCanonicalTerms(length.Value, bindings),
                length.Dimension),
            SymbolicCountTerm count => new SymbolicCountTerm(SubstituteCanonicalTerms(count.Value, bindings)),
            SymbolicBinaryTerm binary => new SymbolicBinaryTerm(
                binary.Operator,
                SubstituteCanonicalTerms(binary.Left, bindings),
                SubstituteCanonicalTerms(binary.Right, bindings)),
            SymbolicConditionalTerm conditional => new SymbolicConditionalTerm(
                SubstituteCanonicalTerms(conditional.Condition, bindings),
                SubstituteCanonicalTerms(conditional.WhenTrue, bindings),
                SubstituteCanonicalTerms(conditional.WhenFalse, bindings)),
            _ => term
        };
    }
    private static SymbolicCondition CreateCanonicalDisjunction(IReadOnlyList<SymbolicCondition> conditions) {
        var result = conditions[0];
        for (var index = 1; index < conditions.Count; index++)
            result = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, result, conditions[index]);
        return result;
    }
    private static bool CanCompareCanonicalTerms(SymbolicTerm left, SymbolicTerm right) => left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
}
