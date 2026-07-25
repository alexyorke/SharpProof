using static SharpProof.Symbolic.Ir.SymbolicStatefulAssignmentTransfer;
namespace SharpProof.Symbolic.Ir;
internal static partial class SymbolicCfgProgramPointStateCollector {
    internal static bool TryApplyOperation(
        ref SymbolicState state,
        IOperation operation,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation,
        bool allowExpressionStatementCompletion,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string assignmentProvenance,
        out ISymbol? invalidatedGuardTarget) {
        invalidatedGuardTarget = null;
        if (operation is ILocalFunctionOperation) return true;
        if (operation is IVariableDeclarationGroupOperation declarations) {
            foreach (var declarator in declarations.Declarations
                         .SelectMany(static declaration => declaration.Declarators)) {
                if (declarator.Initializer?.Value is not { } value)
                    continue;
                if (!TryApplyAssignment(
                        ref state,
                        declarator.Symbol,
                        value,
                        guard,
                        allowGuardedReferenceAssignments,
                        allowGuardMutation,
                        semanticModel,
                        cancellationToken,
                        assignmentProvenance,
                        out var declaratorInvalidatedGuardTarget))
                    return false;
                invalidatedGuardTarget ??= declaratorInvalidatedGuardTarget;
            }
            return true;
        }
        if (allowExpressionStatementCompletion &&
            operation.DescendantsAndSelf().OfType<IFlowCaptureReferenceOperation>().Any() &&
            operation.Syntax.FirstAncestorOrSelf<StatementSyntax>() is { } capturedStatement &&
            capturedStatement is ExpressionStatementSyntax or LocalDeclarationStatementSyntax &&
            semanticModel.GetOperation(capturedStatement, cancellationToken) is { } sourceOperation &&
            !sourceOperation.DescendantsAndSelf().OfType<IFlowCaptureReferenceOperation>().Any())
            return TryApplyOperation(
                ref state,
                sourceOperation,
                guard,
                allowGuardedReferenceAssignments,
                allowGuardMutation,
                allowExpressionStatementCompletion,
                semanticModel,
                cancellationToken,
                assignmentProvenance,
                out invalidatedGuardTarget);
        var expressionOperation = operation is IExpressionStatementOperation expressionStatement
            ? expressionStatement.Operation
            : operation;
        if (expressionOperation is IDeconstructionAssignmentOperation deconstruction)
            return TryApplyDeconstructionAssignment(
                ref state,
                deconstruction,
                guard,
                semanticModel,
                cancellationToken,
                out invalidatedGuardTarget);
        if (expressionOperation is ICoalesceAssignmentOperation coalesce)
            return TryApplyCoalesceAssignment(
                ref state,
                coalesce,
                guard,
                allowGuardedReferenceAssignments,
                allowGuardMutation,
                semanticModel,
                cancellationToken,
                out invalidatedGuardTarget);
        if (expressionOperation is ISimpleAssignmentOperation assignment) {
            if (assignment.IsRef ||
                assignment.Target is ILocalReferenceOperation {
                    Local.IsRef: true
                } ||
                assignment.Target is IArrayElementReferenceOperation {
                    ArrayReference.Type: { } receiverType
                } &&
                SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(receiverType) ||
                assignment.Target is IPropertyReferenceOperation {
                    Property.IsIndexer: true,
                    Instance.Type: { } indexerReceiverType
                } &&
                SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(
                    indexerReceiverType)) {
                state = state.MarkInexact(
                    SymbolicUnknownReason.UnsupportedIrEncoding,
                    new SymbolicLoweringProvenance(
                        "compiler-flow",
                        assignment.Syntax.Span,
                        "aliased-write"));
                invalidatedGuardTarget = null;
                return true;
            }
            return TryGetDirectTarget(assignment.Target, out var target)
                ? TryApplyAssignment(
                    ref state,
                    target,
                    assignment.Value,
                    guard,
                    allowGuardedReferenceAssignments,
                    allowGuardMutation,
                    semanticModel,
                    cancellationToken,
                    assignmentProvenance,
                    out invalidatedGuardTarget)
                : TryApplyExplicitTargetAssignment(
                    ref state,
                    assignment,
                    guard,
                    semanticModel,
                    cancellationToken,
                    out invalidatedGuardTarget);
        }
        var computedUpdate = expressionOperation is IIncrementOrDecrementOperation or ICompoundAssignmentOperation
            ? expressionOperation
            : null;
        var computedTarget = computedUpdate switch {
            IIncrementOrDecrementOperation increment => increment.Target,
            ICompoundAssignmentOperation compound => compound.Target,
            _ => null
        };
        if (computedUpdate != null) {
            if (computedTarget == null ||
                !TryGetDirectTarget(computedTarget, out var computedTargetSymbol) ||
                computedUpdate.Syntax is not ExpressionSyntax expression)
                return false;
            if (!TryApplyComputedUpdate(ref state, computedTargetSymbol, computedUpdate, semanticModel, cancellationToken)) {
                if (guard != null) return false;
                SymbolicStateInvalidator.InvalidateSymbol(ref state, computedTargetSymbol, expression);
                SymbolicStateInvalidator.InvalidateNestedMutations(ref state, expression, semanticModel, cancellationToken);
            }
            if (GuardReferencesTarget(guard, computedTargetSymbol))
                invalidatedGuardTarget = computedTargetSymbol;
            return true;
        }
        if (allowExpressionStatementCompletion &&
            operation is IExpressionStatementOperation
            expressionStatementOperation)
            return TryApplyExpressionStatement(ref state, expressionStatementOperation, guard, semanticModel, cancellationToken);
        return false;
    }
    private static bool TryApplyExpressionStatement(
        ref SymbolicState state,
        IExpressionStatementOperation operation,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (operation.Syntax is not ExpressionStatementSyntax expressionStatement)
            return false;
        var invalidations = SymbolicStateInvalidator.LowerNestedMutations(expressionStatement, semanticModel, cancellationToken);
        if (guard != null &&
            (invalidations.HasUnsupportedMutation || invalidations.Steps
                .SelectMany(static step => step.Targets)
                .Any(target => SymbolicIrReferenceScanner.ContainsVariableOrMember(guard, target.Key))))
            return false;
        state = SymbolicStateInvalidator.ApplyNestedMutationInvalidations(state, invalidations);
        state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
            state,
            expressionStatement.Expression,
            expressionStatement,
            semanticModel,
            cancellationToken);
        var expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expressionStatement.Expression);
        if (expression is InvocationExpressionSyntax invocation &&
            semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
            NullableFlowFacts.HasDoesNotReturn(invocationOperation.TargetMethod))
            state = state.MarkContradictory();
        return true;
    }
    internal static bool TryApplyAssignment(
        ref SymbolicState state,
        ISymbol target,
        IOperation value,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out ISymbol? invalidatedGuardTarget) {
        if (RequiresStructuralAssignmentFallback(target, guard, allowGuardedReferenceAssignments, allowGuardMutation)) {
            invalidatedGuardTarget = null;
            return false;
        }
        invalidatedGuardTarget = GuardReferencesTarget(guard, target) ? target : null;
        if (value.Syntax is not ExpressionSyntax expression)
            return false;
        SymbolicTerm? previousValue = null;
        var isSelfReferential = SymbolMutationFacts.ExpressionReferencesSymbol(expression, target, semanticModel, cancellationToken);
        if (isSelfReferential &&
            (!SymbolicStateValueFacts.TryGetCurrentValue(state, target, out previousValue) ||
             previousValue.Kind != SharpProof.ProofCore.Smt.SmtValueKind.Int)) {
            ApplyUnsupportedSelfReferentialCompletion(ref state, target, expression, semanticModel, cancellationToken, provenance);
            return true;
        }
        return SymbolicOperationTransfer.ApplyAssignment(
            ref state,
            target,
            expression,
            semanticModel,
            cancellationToken,
            provenance: provenance,
            bindingProvenance: provenance + ".assigned-value",
            postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic,
            preInvalidationTargetValue: previousValue);
    }
    private static void ApplyUnsupportedSelfReferentialCompletion(
        ref SymbolicState state,
        ISymbol target,
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance) {
        state = SymbolicStateValueFacts.RemoveReferences(state, target);
        var condition = SymbolicOperationLowerer.LowerThrowGuardedAssignmentPostcondition(
            target,
            SymbolicAssignmentStateTransfer.GetThrowGuardedValue(expression),
            new SymbolicLoweringContext(semanticModel, cancellationToken),
            provenance);
        if (condition != null)
            state = state.AddPathCondition(condition);
    }
    private static bool TryApplyExplicitTargetAssignment(
        ref SymbolicState state,
        ISimpleAssignmentOperation assignment,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol? invalidatedGuardTarget) {
        invalidatedGuardTarget = null;
        if (guard != null || assignment.Syntax is not AssignmentExpressionSyntax syntax)
            return false;
        SymbolicStateInvalidator.InvalidateMutationTarget(ref state, syntax.Left, semanticModel, cancellationToken);
        SymbolicStateInvalidator.InvalidateNestedAssignmentMutations(ref state, syntax, semanticModel, cancellationToken);
        return SymbolicOperationTransfer.ApplyLowering(
            ref state,
            SymbolicOperationLowerer.LowerExplicitTargetAssignment(syntax, new SymbolicLoweringContext(semanticModel, cancellationToken)));
    }
    internal static bool RequiresStructuralAssignmentFallback(
        ISymbol target,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation) {
        var type = target switch {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        };
        return guard != null &&
            type?.IsReferenceType == true &&
            (!allowGuardedReferenceAssignments ||
             !allowGuardMutation && GuardReferencesTarget(guard, target));
    }
    internal static bool GuardReferencesTarget(SymbolicCondition? guard, ISymbol target) =>
        guard != null &&
        SymbolicIrReferenceScanner.ContainsVariableOrMember(guard, SymbolicFactFactory.GetSmtVariableName(target));
    internal static bool TryGetDirectTarget(IOperation operation, out ISymbol target) {
        target = operation switch {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null!
        };
        return target != null;
    }
    private static bool ContainsSite(SyntaxNode container, SyntaxNode site) =>
        container.Span.Contains(site.SpanStart) || site.Span.Contains(container.SpanStart);
    internal static bool IsTargetOperation(
        IOperation operation,
        SyntaxNode site,
        bool includeCurrentStatementCompletionFacts,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (!includeCurrentStatementCompletionFacts || site is not LocalDeclarationStatementSyntax declaration)
            return ContainsSite(operation.Syntax, site);
        if (operation is IVariableDeclarationGroupOperation)
            return ContainsSite(operation.Syntax, declaration);
        ISymbol? target = operation switch {
            IVariableDeclaratorOperation declarator => declarator.Symbol,
            ISimpleAssignmentOperation assignment when TryGetDirectTarget(assignment.Target, out var symbol) =>
                symbol,
            _ => null
        };
        return target != null && declaration.Declaration.Variables.Any(variable =>
            SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(variable, cancellationToken), target));
    }
}
