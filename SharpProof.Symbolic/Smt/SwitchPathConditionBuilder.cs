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
            SymbolicStateFactBuilder.CanCompareIrTerms(governingValue, constant)) {
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
            guardCondition = SymbolicIrSubstitution.ReplaceVariableNames(guardCondition, bindings);
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
    private static SymbolicCondition CreateCanonicalDisjunction(IReadOnlyList<SymbolicCondition> conditions) {
        var result = conditions[0];
        for (var index = 1; index < conditions.Count; index++)
            result = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, result, conditions[index]);
        return result;
    }
}
