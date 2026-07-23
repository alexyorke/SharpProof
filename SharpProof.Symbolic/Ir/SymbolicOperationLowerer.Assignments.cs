namespace SharpProof.Symbolic.Ir;
internal static partial class SymbolicOperationLowerer {
    internal static SymbolicLoweringResult<SymbolicOperationDescriptor> LowerSimpleAssignment(
        ISymbol targetSymbol,
        SyntaxNode source,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext,
        string provenance,
        string? bindingProvenance = null,
        string? evidenceKey = null,
        SymbolicAssignmentPostconditionProfile postconditionProfile =
            SymbolicAssignmentPostconditionProfile.Analyzer) {
        if (source is not ExpressionSyntax valueExpression)
            return Unsupported(source, provenance + ".target");
        var throwGuardedValue = SymbolicAssignmentStateTransfer.GetThrowGuardedValue(valueExpression);
        valueExpression = throwGuardedValue.EffectiveValueExpression;
        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(1);
        var postconditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        if (TryCreateSymbolTerm(targetSymbol, targetContext, out var target)) {
            var value = target.Kind == SmtValueKind.Bool
                ? SymbolicSemanticPipeline.LowerBooleanValueTerm(valueExpression, valueContext)
                : SymbolicSemanticPipeline.LowerTerm(valueExpression, valueContext);
            if (value is { IsExact: true, Value: { } sourceTerm } &&
                SymbolicStateFactBuilder.CanCompareIrTerms(target, sourceTerm)) {
                var isSymbolicReference =
                    postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                    target.Kind == SmtValueKind.Reference;
                bindings.Add(new SymbolicAssignmentBinding(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition),
                    target,
                    sourceTerm,
                    isSymbolicReference ? provenance + ".assigned-reference" : bindingProvenance,
                    isSymbolicReference ? null : evidenceKey,
                    PropagateSourceFacts:
                        postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                        SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                            valueContext.SemanticModel,
                            valueContext.CancellationToken,
                            out _),
                    DeriveIntegerBounds:
                        postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                        target.Kind == SmtValueKind.Int));
            }
            if (target.Kind == SmtValueKind.Reference &&
                NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                    valueExpression, valueContext.SemanticModel, valueContext.CancellationToken))
                postconditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    target,
                    new SymbolicNullTerm(),
                    valueExpression,
                    provenance + ".assigned-non-null",
                    targetSymbol));
            if (postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    target,
                    valueExpression) is { IsExact: true, Value: { } targetLength } &&
                SymbolicSemanticPipeline.LowerBuiltInLengthTerm(valueExpression, valueContext) is {
                    IsExact: true, Value: { } valueLength
                } &&
                SymbolicStateFactBuilder.CanCompareIrTerms(targetLength, valueLength))
                postconditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    targetLength,
                    valueLength,
                    valueExpression,
                    provenance + ".assigned-length"));
        }
        if (postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic)
            AddSymbolicThrowGuardedAssignmentPostconditions(postconditions, targetSymbol, throwGuardedValue, valueContext, provenance);
        if (bindings.Count == 0 && postconditions.Count == 0)
            return Unsupported(source, provenance + ".value");
        var operation = new SymbolicAssignmentOperation(
            bindings.ToImmutable(),
            postconditions.ToImmutable(),
            new SymbolicOperationOrigin(valueExpression.Span, provenance),
            [new SymbolicInvalidationTarget(SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition))]);
        return SymbolicLoweringResult<SymbolicOperationDescriptor>.Exact(
            operation,
            new SymbolicLoweringProvenance("roslyn-to-operation", valueExpression.Span, provenance));
    }
    private static void AddSymbolicThrowGuardedAssignmentPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        SymbolicThrowGuardedValue guardedValue,
        SymbolicLoweringContext valueContext,
        string provenance) {
        var condition = LowerThrowGuardedAssignmentPostcondition(targetSymbol, guardedValue, valueContext, provenance);
        if (condition != null)
            conditions.Add(condition);
    }
    internal static SymbolicCondition? LowerThrowGuardedAssignmentPostcondition(
        ISymbol targetSymbol,
        SymbolicThrowGuardedValue guardedValue,
        SymbolicLoweringContext valueContext,
        string provenance) {
        if (!guardedValue.HasGuard) return null;
        if (guardedValue.GuardExpression is { } guard) {
            var effectiveValueIsTarget =
                SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                    CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(guardedValue.EffectiveValueExpression),
                    valueContext.SemanticModel,
                    valueContext.CancellationToken,
                    out var effectiveValueSymbol) &&
                SymbolEqualityComparer.Default.Equals(effectiveValueSymbol, targetSymbol);
            if (SymbolMutationFacts.ExpressionReferencesSymbol(
                    guard,
                    targetSymbol,
                    valueContext.SemanticModel,
                    valueContext.CancellationToken) &&
                !effectiveValueIsTarget)
                return null;
            return SymbolicSemanticPipeline.LowerBranchCondition(guard, guardedValue.GuardBranchWhenTrue,
                valueContext) is { IsExact: true, Value: { } condition }
                    ? condition
                    : null;
        }
        if (!guardedValue.RequiresNonNullValue ||
            NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                guardedValue.EffectiveValueExpression,
                valueContext.SemanticModel,
                valueContext.CancellationToken) ||
            SymbolicSemanticPipeline.LowerTerm(guardedValue.EffectiveValueExpression, valueContext) is not {
                IsExact: true,
                Value: { Kind: SmtValueKind.Reference } subject
            })
            return null;
        return SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            subject,
            new SymbolicNullTerm(),
            guardedValue.EffectiveValueExpression,
            provenance + ".throw-guard.non-null");
    }
    internal static bool TryGetTupleElementStorageNames(ISymbol symbol, int expectedCount, out string[] elementNames) {
        elementNames = [];
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not INamedTypeSymbol { IsTupleType: true } tupleType ||
            expectedCount > 0 && tupleType.TupleElements.Length != expectedCount)
            return false;
        elementNames = new string[tupleType.TupleElements.Length];
        for (var index = 0; index < elementNames.Length; index++) {
            var field = tupleType.TupleElements[index].CorrespondingTupleField ?? tupleType.TupleElements[index];
            if (string.IsNullOrWhiteSpace(field.Name)) return false;
            elementNames[index] = field.Name;
        }
        return true;
    }
    private static bool TryGetTupleElementType(ISymbol tupleSymbol, string elementName, out ITypeSymbol elementType) {
        if (SymbolicFactFactory.GetTrackedSymbolType(tupleSymbol) is not INamedTypeSymbol { IsTupleType: true } tupleType) {
            elementType = null!;
            return false;
        }
        var element = tupleType.TupleElements.FirstOrDefault(field =>
            string.Equals((field.CorrespondingTupleField ?? field).Name, elementName, StringComparison.Ordinal));
        elementType = element?.Type!;
        return element != null;
    }
    internal static bool TryCreateTupleElementTerm(
        ISymbol tupleSymbol,
        string elementName,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
            !SymbolicFactFactory.TryGetValueKind(
                elementType,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsSymbolicReferenceLikeType,
                out var elementKind)) {
            term = null!;
            return false;
        }
        term = new SymbolicMemberTerm(
            new SymbolicVariableTerm(context.GetVariableName(tupleSymbol), SmtValueKind.Reference),
            elementName,
            elementKind);
        return true;
    }
    internal static SymbolicLoweringResult<SymbolicOperationDescriptor> LowerComputedUpdate(
        ISymbol targetSymbol,
        SymbolicTerm sourceTerm,
        SyntaxNode source,
        SymbolicLoweringContext targetContext,
        string provenance) {
        if (!TryCreateSymbolTerm(targetSymbol, targetContext, out var target) ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(target, sourceTerm) ||
            SymbolicIrReferenceScanner.ContainsVariableOrMember(
                sourceTerm,
                SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition)))
            return Unsupported(source, provenance + ".value");
        var bindings = System.Collections.Immutable.ImmutableArray.Create(
                new SymbolicAssignmentBinding(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition),
                    target,
                    sourceTerm,
                    provenance));
        var origin = new SymbolicOperationOrigin(source.Span, provenance);
        var operation = new SymbolicMutationOperation(
            bindings,
            [],
            origin);
        return SymbolicLoweringResult<SymbolicOperationDescriptor>.Exact(
            operation,
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
    }
    internal static SymbolicLoweringResult<SymbolicOperationDescriptor> LowerExplicitTargetAssignment(
        AssignmentExpressionSyntax assignment,
        SymbolicLoweringContext context) {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return Unsupported(assignment, "ir.path.prior-statement.explicit-target.kind");
        var left = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(assignment.Left);
        var isMember = SymbolicStateInvalidator.IsCurrentInstanceMemberReference(left, context.SemanticModel, context.CancellationToken);
        if (!isMember && left is not ElementAccessExpressionSyntax)
            return Unsupported(assignment, "ir.path.prior-statement.explicit-target.shape");
        if (left is ElementAccessExpressionSyntax element &&
            SymbolicAssignmentStateTransfer.ExpressionReferencesAnySymbol(
                assignment.Right,
                SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
                    element.Expression,
                    context.SemanticModel,
                    context.CancellationToken),
                context.SemanticModel,
                context.CancellationToken))
            return Unsupported(assignment, "ir.path.prior-statement.explicit-target.element");
        if (SymbolicSemanticPipeline.LowerTerm(left, context) is not
            { IsExact: true, Value: { } target })
            return Unsupported(assignment, "ir.path.prior-statement.explicit-target.term");
        var value = isMember
            ? SymbolicAssignmentStateTransfer.GetThrowGuardedValue(assignment.Right).EffectiveValueExpression
            : assignment.Right;
        var provenance = isMember
            ? "ir.path.prior-statement.member"
            : "ir.path.prior-statement.element-assignment";
        return LowerExplicitTargetAssignment(
            target,
            value,
            isMember ? value : assignment,
            context,
            provenance,
            isMember ? provenance + ".assigned-value" : provenance,
            includeReferencePostconditions: isMember);
    }
    internal static SymbolicLoweringResult<SymbolicOperationDescriptor> LowerExplicitTargetAssignment(
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SyntaxNode source,
        SymbolicLoweringContext context,
        string provenance,
        string bindingProvenance,
        bool includeReferencePostconditions) {
        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(1);
        var postconditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        if (SymbolicSemanticPipeline.LowerTerm(valueExpression, context) is { IsExact: true, Value: { } value } &&
            SymbolicStateFactBuilder.CanCompareIrTerms(target, value))
            bindings.Add(new SymbolicAssignmentBinding(
                SymbolicState.CreateProofTermKey(target),
                target,
                value,
                bindingProvenance,
                InvalidateTarget: false));
        if (includeReferencePostconditions && target.Kind == SmtValueKind.Reference) {
            if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(valueExpression, context.SemanticModel, context.CancellationToken))
                postconditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    target,
                    new SymbolicNullTerm(),
                    valueExpression,
                    provenance + ".assigned-non-null"));
        }
        if (bindings.Count == 0 && postconditions.Count == 0)
            return Unsupported(source, provenance + ".value");
        return SymbolicLoweringResult<SymbolicOperationDescriptor>.Exact(
            new SymbolicAssignmentOperation(
                bindings.ToImmutable(),
                postconditions.ToImmutable(),
                new SymbolicOperationOrigin(source.Span, provenance)),
            new SymbolicLoweringProvenance("explicit-target-assignment", source.Span, provenance));
    }
    internal static SymbolicLoweringResult<SymbolicOperationDescriptor> LowerCoalesceAssignment(
        ISymbol targetSymbol,
        ExpressionSyntax rightExpression,
        SymbolicLoweringContext context,
        string provenance) {
        SymbolicCondition postcondition;
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(rightExpression) is ThrowExpressionSyntax) {
            if (SymbolicNullableLowerer.TryCreateSymbolTerms(targetSymbol, context, out var hasValue, out _))
                postcondition = SymbolicIrLowerer.CreateTruthCondition(
                    hasValue, rightExpression, provenance + ".throw-completion-has-value", targetSymbol);
            else if (TryCreateSymbolTerm(targetSymbol, context, out var reference) &&
                     reference.Kind == SmtValueKind.Reference)
                postcondition = SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    reference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    provenance + ".throw-completion-non-null",
                    targetSymbol);
            else
                return Unsupported(rightExpression, provenance + ".target");
        }
        else if (SymbolicNullableLowerer.TryCreateSymbolTerms(targetSymbol, context, out var targetHasValue, out var targetValue)) {
            var hasValue = SymbolicSemanticPipeline.LowerNullableHasValueTerm(rightExpression, context);
            SymbolicTerm? rightHasValue = hasValue is { IsExact: true, Value: { } loweredHasValue }
                ? loweredHasValue
                : SymbolicSemanticPipeline.LowerTerm(rightExpression, context) is {
                    IsExact: true,
                    Value: { } rightValue
                } && rightValue.Kind == targetValue.Kind
                    ? new SymbolicBooleanConstantTerm(true)
                    : null;
            if (rightHasValue == null) return Unsupported(rightExpression, provenance + ".value");
            postcondition = rightHasValue is SymbolicBooleanConstantTerm { Value: true }
                ? SymbolicIrLowerer.CreateTruthCondition(
                    targetHasValue, rightExpression, provenance + ".nullable-has-value", targetSymbol)
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    SymbolicIrLowerer.CreateTruthCondition(
                        targetHasValue, rightExpression, provenance + ".target-has-value", targetSymbol),
                    new SymbolicNotCondition(SymbolicIrLowerer.CreateTruthCondition(
                        rightHasValue, rightExpression, provenance + ".right-has-value")));
        }
        else if (TryCreateSymbolTerm(targetSymbol, context, out var target) &&
                 target.Kind == SmtValueKind.Reference) {
            var definitelyNonNull = NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                rightExpression,
                context.SemanticModel,
                context.CancellationToken);
            var targetNonNull = SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm(),
                rightExpression,
                provenance + (definitelyNonNull ? ".non-null" : ".target-non-null"),
                targetSymbol);
            if (definitelyNonNull)
                postcondition = targetNonNull;
            else if (SymbolicSemanticPipeline.LowerReferenceTerm(rightExpression, context) is { IsExact: true, Value: { } right })
                postcondition = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    targetNonNull,
                    SymbolicIrLowerer.CreateRelationCondition(
                        SymbolicRelationOperator.Equal,
                        target,
                        right,
                        rightExpression,
                        provenance + ".target-equals-right",
                        targetSymbol));
            else
                return Unsupported(rightExpression, provenance + ".value");
        }
        else {
            return Unsupported(rightExpression, provenance + ".target");
        }
        var operation = new SymbolicAssignmentOperation(
            [],
            [postcondition],
            new SymbolicOperationOrigin(rightExpression.Span, provenance));
        return SymbolicLoweringResult<SymbolicOperationDescriptor>.Exact(
            operation,
            new SymbolicLoweringProvenance("roslyn-to-operation", rightExpression.Span, provenance));
    }
    private static bool TryCreateSymbolTerm(ISymbol symbol, SymbolicLoweringContext context, out SymbolicTerm term) {
        var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);
        if (type == null ||
            !SymbolicFactFactory.TryGetValueKind(
                type,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsSymbolicReferenceLikeType,
                out var kind)) {
            term = null!;
            return false;
        }
        term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
        return true;
    }
    private static SymbolicLoweringResult<SymbolicOperationDescriptor> Unsupported(SyntaxNode source, string provenance)
        => SymbolicLoweringResult<SymbolicOperationDescriptor>.Unsupported(
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
}
