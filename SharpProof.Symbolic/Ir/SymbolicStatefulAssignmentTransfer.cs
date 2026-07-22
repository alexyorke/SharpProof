using System.Numerics;
using static SharpProof.Symbolic.Ir.SymbolicCfgProgramPointStateCollector;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;
namespace SharpProof.Symbolic.Ir;
internal static class SymbolicStatefulAssignmentTransfer {
    internal static bool TryApplyCoalesceAssignment(
        ref SymbolicState state,
        ICoalesceAssignmentOperation assignment,
        SymbolicCondition? guard,
        bool allowGuardedReferenceAssignments,
        bool allowGuardMutation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol? invalidatedGuardTarget) {
        invalidatedGuardTarget = null;
        if (assignment.Syntax is not AssignmentExpressionSyntax syntax)
            return false;
        if (!TryGetDirectTarget(assignment.Target, out var target)) {
            if (guard != null)
                return false;
            SymbolicStateInvalidator.InvalidateMutationTarget(ref state, syntax.Left, semanticModel, cancellationToken);
            return true;
        }
        if (RequiresStructuralAssignmentFallback(target, guard, allowGuardedReferenceAssignments, allowGuardMutation))
            return false;
        invalidatedGuardTarget = GuardReferencesTarget(guard, target) ? target : null;
        target = target.OriginalDefinition;
        if (SymbolicStateValueFacts.IsKnownNonNullReference(state, target) ||
            SymbolicStateValueFacts.IsKnownNullableHasValue(state, target))
            return true;
        SymbolicStateValueFacts.TryGetCurrentValue(state, target, out var previousValue);
        SymbolicOperationTransitionResult transition;
        if (SymbolicStateValueFacts.IsKnownNullReference(state, target) ||
            SymbolicStateValueFacts.IsKnownNullableNoValue(state, target))
            transition = SymbolicOperationTransfer.ApplyAssignment(
                state,
                target,
                assignment.Value.Syntax,
                semanticModel,
                cancellationToken,
                provenance: "ir.path.prior-statement.coalesce-assignment",
                bindingProvenance: "ir.path.prior-statement.coalesce-assignment.assigned-value",
                asExpressionProvenanceRoot: "ir.path.prior-statement.coalesce-assignment.as",
                postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic,
                preInvalidationTargetValue: previousValue);
        else if (assignment.Value.Syntax is ExpressionSyntax right)
            transition = SymbolicOperationTransfer.ApplyCoalesceAssignment(
                state,
                target,
                right,
                semanticModel,
                cancellationToken,
                "ir.path.coalesce-assignment");
        else
            return false;
        if (!transition.IsExact)
            return false;
        state = transition.State;
        return true;
    }
    internal static bool TryApplyDeconstructionAssignment(
        ref SymbolicState state,
        IDeconstructionAssignmentOperation assignment,
        SymbolicCondition? guard,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol? invalidatedGuardTarget) {
        invalidatedGuardTarget = null;
        if (guard != null || assignment.Syntax is not AssignmentExpressionSyntax syntax ||
            !SymbolicDeconstructionPlan.TryCollectTargets(
                assignment.Target,
                operation => ResolveDeconstructionTarget(operation, semanticModel, cancellationToken),
                out var targets))
            return false;
        var targetSymbols = targets.Select(static target => target.Symbol).ToArray();
        foreach (var target in targetSymbols)
            if (target != null)
                state = SymbolicStateValueFacts.RemoveReferences(state, target);
        var nonDiscardTargets = targetSymbols.Where(static target => target != null).Cast<ISymbol>().ToArray();
        if (nonDiscardTargets.Length == 0 ||
            SymbolicAssignmentStateTransfer.ExpressionReferencesAnySymbol(syntax.Right, nonDiscardTargets, semanticModel,
                cancellationToken))
            return true;
        var right = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(syntax.Right);
        if (right is TupleExpressionSyntax rightTuple) {
            if (rightTuple.Arguments.Count != targetSymbols.Length)
                return true;
            for (var index = 0; index < targetSymbols.Length; index++)
                if (targetSymbols[index] != null &&
                    semanticModel.GetOperation(rightTuple.Arguments[index].Expression, cancellationToken) is { } elementValue)
                    TryApplyAssignment(
                        ref state,
                        targetSymbols[index]!,
                        elementValue,
                        guard: null,
                        allowGuardedReferenceAssignments: true,
                        allowGuardMutation: true,
                        semanticModel,
                        cancellationToken,
                        "ir.path.prior-statement.tuple-target",
                        out _);
            return true;
        }
        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(right, semanticModel, cancellationToken, out var sourceSymbol) ||
            !SymbolicOperationLowerer.TryGetTupleElementStorageNames(sourceSymbol, targetSymbols.Length, out var sourceElementNames))
            return true;
        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(targetSymbols.Length);
        for (var index = 0; index < targetSymbols.Length; index++) {
            var target = targetSymbols[index];
            if (target == null ||
                !TryCreateSymbolTerm(target, out var targetTerm) ||
                !SymbolicOperationLowerer.TryCreateTupleElementTerm(
                    sourceSymbol,
                    sourceElementNames[index],
                    new SymbolicLoweringContext(semanticModel, cancellationToken),
                    out var sourceTerm) ||
                !CanCompareIrTerms(targetTerm, sourceTerm))
                continue;
            bindings.Add(new SymbolicAssignmentBinding(
                SymbolicFactFactory.GetSmtVariableName(target),
                targetTerm,
                sourceTerm,
                "ir.path.prior-statement.tuple-target.assigned-value",
                PropagateSourceFacts: true));
        }
        if (bindings.Count == 0)
            return true;
        var transition = SymbolicOperationTransfer.ApplyBindings(
            state,
            bindings.ToImmutable(),
            right,
            "ir.path.prior-statement.tuple-target");
        if (transition.IsExact)
            state = transition.State;
        return true;
    }
    private static ISymbol? ResolveDeconstructionTarget(
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => operation switch {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ when operation.Syntax is SingleVariableDesignationSyntax designation =>
                semanticModel.GetDeclaredSymbol(designation, cancellationToken),
            _ when operation.Syntax is IdentifierNameSyntax identifier =>
                semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
            _ => null
        };
    internal static bool TryApplyComputedUpdate(
        ref SymbolicState state,
        ISymbol target,
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var targetType = SymbolicFactFactory.GetTrackedSymbolType(target);
        if (targetType is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
            targetType = underlying;
        if (operation.Syntax is not ExpressionSyntax source ||
            !SymbolicStateValueFacts.TryGetCurrentValue(state, target, out var previous) ||
            previous.Kind != SharpProof.ProofCore.Smt.SmtValueKind.Int ||
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(targetType, out var minimum, out var maximum))
            return false;
        SymbolicBinaryTermOperator binaryOperator;
        SymbolicTerm right;
        bool isChecked;
        string provenance;
        if (operation is IIncrementOrDecrementOperation { OperatorMethod: null } increment) {
            var isIncrement = increment.Kind == OperationKind.Increment;
            binaryOperator = isIncrement ? SymbolicBinaryTermOperator.Add : SymbolicBinaryTermOperator.Subtract;
            right = new SymbolicIntegerConstantTerm(1);
            isChecked = increment.IsChecked;
            provenance = isIncrement
                ? "ir.path.prior-statement.increment"
                : "ir.path.prior-statement.decrement";
        }
        else if (operation is ICompoundAssignmentOperation { OperatorMethod: null } compound &&
                 source is AssignmentExpressionSyntax assignment &&
                 CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignment.Kind(), out var binaryKind) &&
                 SymbolicOperatorLowerer.TryGetBinaryTermOperator(binaryKind, out binaryOperator) &&
                 SymbolicSemanticPipeline.LowerTerm(assignment.Right, new SymbolicLoweringContext(semanticModel,
                     cancellationToken)) is { IsExact: true, Value: { Kind: SharpProof.ProofCore.Smt.SmtValueKind.Int } loweredRight }) {
            right = loweredRight;
            isChecked = compound.IsChecked;
            provenance = "ir.path.prior-statement.compound-assignment";
            var targetName = SymbolicFactFactory.GetSmtVariableName(target);
            if (SymbolicIrReferenceScanner.ContainsVariableOrMember(previous, targetName) ||
                SymbolicIrReferenceScanner.ContainsVariableOrMember(right, targetName))
                return false;
        }
        else
            return false;
        SymbolicTerm updated;
        if (previous is SymbolicIntegerConstantTerm leftConstant &&
            right is SymbolicIntegerConstantTerm rightConstant) {
            if (!TryEvaluateConstantUpdate(
                    leftConstant.Value,
                    rightConstant.Value,
                    binaryOperator,
                    minimum,
                    maximum,
                    isChecked,
                    out updated))
                return false;
        }
        else {
            if (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder &&
                right is SymbolicIntegerConstantTerm { Value: 0 })
                return false;
            var mathematical = new SymbolicBinaryTerm(binaryOperator, previous, right);
            if (binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract or
                SymbolicBinaryTermOperator.Multiply)
                updated = SymbolicIrLowerer.CreateOverflowAwareBinaryTerm(mathematical, minimum, maximum, source, provenance, isChecked);
            else updated = minimum < 0
                ? new SymbolicConditionalTerm(
                    SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(
                        previous, right, minimum, source, provenance + ".signed-division-overflow"),
                    mathematical with { MayOverflow = true },
                    mathematical)
                : mathematical;
        }
        var transition = SymbolicOperationTransfer.ApplyComputedUpdate(
            state,
            target,
            updated,
            source,
            semanticModel,
            cancellationToken,
            provenance);
        if (!transition.IsExact)
            return false;
        state = transition.State;
        return true;
    }
    private static bool TryEvaluateConstantUpdate(
        long left,
        long right,
        SymbolicBinaryTermOperator operation,
        long minimum,
        long maximum,
        bool isChecked,
        out SymbolicTerm value) {
        value = null!;
        if (operation is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder &&
            (right == 0 || minimum < 0 && left == minimum && right == -1))
            return false;
        var result = operation switch {
            SymbolicBinaryTermOperator.Add => (BigInteger)left + right,
            SymbolicBinaryTermOperator.Subtract => (BigInteger)left - right,
            SymbolicBinaryTermOperator.Multiply => (BigInteger)left * right,
            SymbolicBinaryTermOperator.Divide => left / (BigInteger)right,
            SymbolicBinaryTermOperator.Remainder => left % (BigInteger)right,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
        if (result < minimum || result > maximum) {
            if (isChecked)
                return false;
            var modulus = (BigInteger)maximum - minimum + 1;
            result = ((result - minimum) % modulus + modulus) % modulus + minimum;
        }
        value = new SymbolicIntegerConstantTerm((long)result);
        return true;
    }
}
