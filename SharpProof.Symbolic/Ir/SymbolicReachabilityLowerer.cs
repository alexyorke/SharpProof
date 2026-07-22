using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicReachabilityLowerer {
    internal static SymbolicOperationTransitionResult Apply(
        SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        condition = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition);
        if (branchWhenTrue &&
            condition is BinaryExpressionSyntax andCondition &&
            andCondition.IsKind(SyntaxKind.LogicalAndExpression))
            return ApplyBoth(state, andCondition.Left, andCondition.Right, branchWhenTrue: true, semanticModel, cancellationToken);
        if (!branchWhenTrue &&
            condition is BinaryExpressionSyntax orCondition &&
            orCondition.IsKind(SyntaxKind.LogicalOrExpression))
            return ApplyBoth(state, orCondition.Left, orCondition.Right, branchWhenTrue: false, semanticModel, cancellationToken);

        if (TryApplyInlineAssignment(state, condition, branchWhenTrue, semanticModel, cancellationToken, out var inlineTransition))
            return inlineTransition;

        var transition = ApplyCondition(state, condition, branchWhenTrue, semanticModel, cancellationToken);
        return transition.IsExact
            ? ApplyPatternBinding(transition.State, condition, branchWhenTrue, semanticModel, cancellationToken)
            : transition;
    }
    internal static SymbolicOperationTransitionResult ApplyCondition(
        SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion = null) {
        var lowering = SymbolicSemanticPipeline.LowerBranchCondition(
            condition,
            branchWhenTrue,
            new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion));
        if (lowering is not { IsExact: true, Value: { } exactCondition })
            return Unsupported(state, condition, "condition");

        return SymbolicOperationTransferKernel.Assume(
            state,
            exactCondition,
            assumeTrue: true,
            condition.Span,
            "operation-transfer.branch-assumption");
    }
    internal static SymbolicOperationTransitionResult ApplyCondition(
        SymbolicState state,
        IOperation condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition branchCondition,
        Func<ISymbol, int>? getSymbolVersion = null) {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
        SyntaxNode source;
        if (condition is IIsNullOperation { Operand.Syntax: ExpressionSyntax operand } &&
            SymbolicSemanticPipeline.LowerTerm(operand, context) is { IsExact: true, Value: { Kind: SmtValueKind.Reference } subject }) {
            source = operand;
            branchCondition = SymbolicIrLowerer.CreateRelationCondition(
                branchWhenTrue
                    ? SymbolicRelationOperator.Equal
                    : SymbolicRelationOperator.NotEqual,
                subject,
                new SymbolicNullTerm(),
                operand,
                "operation-transfer.branch-null-assumption");
        }
        else if (condition is IIsNullOperation { Operand.Syntax: ExpressionSyntax nullableOperand } &&
                 SymbolicSemanticPipeline.LowerNullableHasValueTerm(nullableOperand, context) is { IsExact: true, Value: { } hasValue }) {
            source = nullableOperand;
            var hasValueCondition = SymbolicIrLowerer.CreateFactCondition(
                new SymbolicTruthAtom(hasValue),
                nullableOperand,
                "operation-transfer.branch-nullable-assumption");
            branchCondition = branchWhenTrue
                ? new SymbolicNotCondition(hasValueCondition)
                : hasValueCondition;
        }
        else if (condition.Syntax is ExpressionSyntax expression &&
                 SymbolicSemanticPipeline.LowerBranchCondition(expression, branchWhenTrue,
                     context) is { IsExact: true, Value: { } lowered }) {
            source = expression;
            branchCondition = lowered;
        }
        else {
            branchCondition = null!;
            return Unsupported(state, condition.Syntax, "condition-operation");
        }
        return SymbolicOperationTransferKernel.Assume(
            state,
            branchCondition,
            assumeTrue: true,
            source.Span,
            "operation-transfer.branch-assumption");
    }
    private static SymbolicOperationTransitionResult ApplyBoth(
        SymbolicState state,
        ExpressionSyntax first,
        ExpressionSyntax second,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var firstTransition = Apply(state, first, branchWhenTrue, semanticModel, cancellationToken);
        var secondTransition = Apply(
            firstTransition.IsExact ? firstTransition.State : state,
            second,
            branchWhenTrue,
            semanticModel,
            cancellationToken);
        if (secondTransition.IsExact)
            return secondTransition;
        return firstTransition.IsExact
            ? firstTransition
            : Unsupported(state, first, "composite");
    }
    internal static bool TryApplyInlineAssignment(
        SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicOperationTransitionResult transition) {
        if (condition is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression))
            return TryApplyInlineAssignment(state, negation.Operand, !branchWhenTrue, semanticModel, cancellationToken, out transition);

        if (condition is not BinaryExpressionSyntax comparison ||
            !SymbolicOperatorLowerer.TryGetRelationOperator(comparison.Kind(), out var relation)) {
            transition = null!;
            return false;
        }
        var leftAssignment = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(comparison.Left) as
            AssignmentExpressionSyntax;
        var rightAssignment = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(comparison.Right) as
            AssignmentExpressionSyntax;
        if (leftAssignment is null == rightAssignment is null) {
            transition = null!;
            return false;
        }
        var assignmentIsLeft = leftAssignment != null;
        var assignment = assignmentIsLeft ? leftAssignment! : rightAssignment!;
        var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            assignedSymbol is not ILocalSymbol and not IParameterSymbol) {
            transition = null!;
            return false;
        }
        assignedSymbol = assignedSymbol.OriginalDefinition;
        var sibling = assignmentIsLeft ? comparison.Right : comparison.Left;
        if (!assignmentIsLeft &&
            SymbolMutationFacts.ExpressionReferencesSymbol(sibling, assignedSymbol, semanticModel, cancellationToken)) {
            transition = Unsupported(state, comparison, "assignment-order");
            return true;
        }
        var effectiveValue = SymbolicAssignmentStateTransfer
            .GetThrowGuardedValue(assignment.Right)
            .EffectiveValueExpression;
        var selfReferential = SymbolMutationFacts.ExpressionReferencesSymbol(
            effectiveValue,
            assignedSymbol,
            semanticModel,
            cancellationToken);
        if (!TryCreateAssignedValueTerm(
                state,
                assignedSymbol,
                effectiveValue,
                selfReferential,
                semanticModel,
                cancellationToken,
                out var assignedValue) ||
            !TryCreateSymbolTerm(assignedSymbol, out var assignedTerm) ||
            SymbolicSemanticPipeline.LowerTerm(sibling, new SymbolicLoweringContext(semanticModel, cancellationToken)) is not
                { IsExact: true, Value: { } siblingTerm }) {
            transition = Unsupported(state, comparison, "assignment-value");
            return true;
        }
        var assignmentTransition = selfReferential
            ? SymbolicOperationTransfer.ApplyComputedUpdate(
                state,
                assignedSymbol,
                assignedValue,
                assignment.Right,
                semanticModel,
                cancellationToken,
                "ir.path.inline-assignment")
            : SymbolicOperationTransfer.ApplyAssignment(
                state,
                assignedSymbol,
                assignment.Right,
                semanticModel,
                cancellationToken,
                provenance: "ir.path.inline-assignment",
                bindingProvenance: "ir.path.inline-assignment.assigned-value",
                postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic);
        if (!assignmentTransition.IsExact) {
            transition = assignmentTransition;
            return true;
        }
        var left = assignmentIsLeft ? assignedTerm : siblingTerm;
        var right = assignmentIsLeft ? siblingTerm : assignedTerm;
        if (!CanCompareIrTerms(left, right)) {
            transition = Unsupported(state, comparison, "comparison");
            return true;
        }
        var comparisonCondition = SymbolicIrLowerer.CreateRelationCondition(
            relation,
            left,
            right,
            comparison,
            "ir.path.inline-assignment.comparison");
        transition = SymbolicOperationTransferKernel.Assume(
            assignmentTransition.State,
            comparisonCondition,
            branchWhenTrue,
            comparison.Span,
            "ir.path.inline-assignment.comparison");
        return true;
    }
    private static bool TryCreateAssignedValueTerm(
        SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax effectiveValue,
        bool selfReferential,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm value) {
        if (selfReferential) {
            value = null!;
            return SymbolicStateValueFacts.TryGetCurrentValue(state, assignedSymbol, out var previous) &&
                SymbolicAssignmentStateTransfer.TryCreateSelfReferentialAssignedValueStateTerm(
                    previous,
                    assignedSymbol,
                    effectiveValue,
                    semanticModel,
                    cancellationToken,
                    out value);
        }
        var lowering = SymbolicSemanticPipeline.LowerTerm(effectiveValue, new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is { IsExact: true, Value: { } lowered }) {
            value = lowered;
            return true;
        }
        if (TryCreateSymbolTerm(assignedSymbol, out var assignedTerm) &&
            assignedTerm.Kind == SmtValueKind.Reference &&
            effectiveValue is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(SyntaxKind.AsExpression)) {
            value = assignedTerm;
            return true;
        }
        value = null!;
        return false;
    }
    private static SymbolicOperationTransitionResult ApplyPatternBinding(
        SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        condition = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition);
        if (condition is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression))
            return ApplyPatternBinding(state, negation.Operand, !branchWhenTrue, semanticModel, cancellationToken);
        if (!branchWhenTrue || condition is not IsPatternExpressionSyntax pattern)
            return Exact(state, condition, "no-pattern");

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var term = SymbolicSemanticPipeline.LowerTerm(pattern.Expression, context);
        var typeInfo = semanticModel.GetTypeInfo(pattern.Expression, cancellationToken);
        if (term is not { IsExact: true, Value: { } matchedTerm } ||
            SymbolicSemanticPipeline.LowerPatternCondition(
                matchedTerm,
                typeInfo.ConvertedType ?? typeInfo.Type,
                pattern.Pattern,
                pattern.Pattern,
                context) is not { IsExact: true, Value: { } patternCondition })
            return Exact(state, condition, "pattern-unsupported");

        return SymbolicOperationTransferKernel.Assume(
            state,
            patternCondition,
            assumeTrue: true,
            pattern.Pattern.Span,
            "cfg-program-point.pattern-binding");
    }
    private static SymbolicOperationTransitionResult Exact(SymbolicState state, SyntaxNode source, string detail) =>
        SymbolicOperationTransitionResult.Exact(
            state,
            ImmutableArray.Create(new SymbolicLoweringProvenance("reachability", source.Span, detail)));

    private static SymbolicOperationTransitionResult Unsupported(SymbolicState state, SyntaxNode source, string detail) =>
        SymbolicOperationTransitionResult.Unsupported(
            state,
            SymbolicUnknownReason.UnsupportedIrEncoding,
            ImmutableArray.Create(new SymbolicLoweringProvenance("reachability", source.Span, detail)));
}
