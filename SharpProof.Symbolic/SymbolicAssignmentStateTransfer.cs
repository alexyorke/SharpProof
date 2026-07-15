using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal readonly record struct SymbolicThrowGuardedValue(
    bool HasGuard,
    ExpressionSyntax EffectiveValueExpression,
    ExpressionSyntax? GuardExpression,
    bool GuardBranchWhenTrue,
    bool RequiresNonNullValue);

internal static class SymbolicAssignmentStateTransfer
{
    internal static void AddVariableDeclarationInitializerStateFacts(
        ref SymbolicState state,
        VariableDeclarationSyntax declaration,
        StatementSyntax completionScope,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        foreach (var declarator in declaration.Variables)
        {
            if (declarator.Initializer == null) continue;

            var initializer = declarator.Initializer.Value;
            SymbolicStateInvalidator.InvalidateNestedMutations(
                ref state,
                initializer,
                semanticModel,
                cancellationToken);
            if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                AddAssignedValueStateFacts(
                    ref state,
                    localSymbol.OriginalDefinition,
                    initializer,
                    semanticModel,
                    cancellationToken,
                    provenanceRoot);

            SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                ref state,
                initializer,
                completionScope,
                false,
                semanticModel,
                cancellationToken);
        }
    }

    internal static void AddAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot,
        SymbolicTerm? previousValueOverride = null)
    {
        var previousValueTerm = previousValueOverride;
        var hadPreviousValueTerm = previousValueTerm != null ||
                                   SymbolicStateValueFacts.TryGetCurrentValue(
                                       state,
                                       assignedSymbol,
                                       out previousValueTerm);
        state = SymbolicStateValueFacts.RemoveReferences(state, assignedSymbol);

        var throwGuardedValue = GetThrowGuardedValue(valueExpression);
        var effectiveValueExpression = throwGuardedValue.EffectiveValueExpression;
        var effectiveValueIsAssignedSymbol =
            SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(effectiveValueExpression),
                semanticModel,
                cancellationToken,
                out var effectiveValueSymbol) &&
            SymbolEqualityComparer.Default.Equals(effectiveValueSymbol, assignedSymbol);
        var isSelfReferential = SymbolMutationFacts.ExpressionReferencesSymbol(
            effectiveValueExpression,
            assignedSymbol,
            semanticModel,
            cancellationToken);
        SymbolicTerm? selfReferentialValueTerm = null;
        if (isSelfReferential &&
            (!hadPreviousValueTerm ||
             !TryCreateSelfReferentialAssignedValueStateTerm(
                 previousValueTerm,
                 assignedSymbol,
                 effectiveValueExpression,
                 semanticModel,
                 cancellationToken,
                 out selfReferentialValueTerm)))
        {
            AddThrowGuardedAssignmentCompletionStateFacts(
                ref state,
                assignedSymbol,
                throwGuardedValue,
                effectiveValueIsAssignedSymbol,
                semanticModel,
                cancellationToken,
                provenanceRoot);
            return;
        }

        var assignedType = SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol);
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var isAsExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(
            effectiveValueExpression) is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(SyntaxKind.AsExpression);
        if (!isSelfReferential &&
            (isAsExpression ||
             assignedType != null &&
             (SymbolicTypeFacts.IsSymbolicReferenceLikeType(assignedType) ||
              SymbolicTypeFacts.IsNullableType(assignedType) ||
              assignedType is INamedTypeSymbol { IsTupleType: true } ||
              assignedType.SpecialType == SpecialType.System_Boolean ||
              SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType(assignedType))))
        {
            var transition = SymbolicOperationTransferAdapter.ApplyAssignment(
                state,
                assignedSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken,
                provenance: provenanceRoot,
                bindingProvenance: provenanceRoot + ".assigned-value",
                asExpressionProvenanceRoot: provenanceRoot + ".as",
                postconditionProfile: SymbolicAssignmentPostconditionProfile.Symbolic);
            if (transition.IsExact)
                state = transition.State;
        }

        if (!isSelfReferential)
            AddSwitchExpressionAssignedValueStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken,
                provenanceRoot);

        if (isSelfReferential &&
                 TryCreateSymbolTerm(assignedSymbol, out var targetTerm) &&
                 selfReferentialValueTerm != null &&
                 selfReferentialValueTerm.Kind == targetTerm.Kind &&
                 CanCompareIrTerms(targetTerm, selfReferentialValueTerm))
        {
            var transition = SymbolicOperationTransferAdapter.ApplyBindings(
                state,
                ImmutableArray.Create(new SymbolicAssignmentBinding(
                    SymbolicFactFactory.GetSmtVariableName(assignedSymbol),
                    targetTerm,
                    selfReferentialValueTerm,
                    provenanceRoot + ".assigned-value",
                    DeriveIntegerBounds: true)),
                effectiveValueExpression,
                SymbolicAssignmentOperationKind.Simple,
                provenanceRoot);
            if (transition.IsExact)
                state = transition.State;
        }

        if (isSelfReferential &&
            TryCreateSymbolTerm(assignedSymbol, out var selfReferenceTarget))
            foreach (var condition in SymbolicOperationLowerer.LowerSymbolicReferenceBackedPostconditions(
                         selfReferenceTarget,
                         effectiveValueExpression,
                         context,
                         provenanceRoot))
                state = state.AddPathCondition(condition);

        if (throwGuardedValue.HasGuard)
            AddThrowGuardedAssignmentCompletionStateFacts(
                ref state,
                assignedSymbol,
                throwGuardedValue,
                effectiveValueIsAssignedSymbol,
                semanticModel,
                cancellationToken,
                provenanceRoot);
    }

    private static void AddThrowGuardedAssignmentCompletionStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        SymbolicThrowGuardedValue throwGuardedValue,
        bool effectiveValueIsAssignedSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (throwGuardedValue.GuardExpression is { } guardExpression)
        {
            if (!SymbolMutationFacts.ExpressionReferencesSymbol(
                    guardExpression,
                    assignedSymbol,
                    semanticModel,
                    cancellationToken) ||
                effectiveValueIsAssignedSymbol)
                SymbolicProgramPointFacts.AddReachabilityCondition(
                    ref state,
                    guardExpression,
                    throwGuardedValue.GuardBranchWhenTrue,
                    semanticModel,
                    cancellationToken);

            return;
        }

        if (!throwGuardedValue.RequiresNonNullValue) return;

        SymbolicProgramPointFacts.AddReferenceNullCondition(
            ref state,
            throwGuardedValue.EffectiveValueExpression,
            false,
            semanticModel,
            cancellationToken,
            provenanceRoot + ".throw-guard.non-null");
    }

    internal static void AddAssignedCurrentInstanceMemberStateFacts(
        ref SymbolicState state,
        SymbolicTerm targetTerm,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        var effectiveValueExpression = GetThrowGuardedValue(valueExpression).EffectiveValueExpression;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (SymbolicSemanticPipeline.LowerTerm(effectiveValueExpression, context) is
            { IsExact: true, Value: { } assignedValueTerm } &&
            assignedValueTerm.Kind == targetTerm.Kind &&
            CanCompareIrTerms(targetTerm, assignedValueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                assignedValueTerm,
                effectiveValueExpression,
                provenanceRoot + ".member.assigned-value");

        AddMemberNonNullStateFact(
            ref state,
            targetTerm,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot + ".member");
        foreach (var condition in SymbolicOperationLowerer.LowerSymbolicReferenceBackedPostconditions(
                     targetTerm,
                     effectiveValueExpression,
                     context,
                     provenanceRoot + ".member"))
            state = state.AddPathCondition(condition);
    }

    internal static bool TryCreateSelfReferentialAssignedValueStateTerm(
        SymbolicTerm previousValueTerm,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm updatedValueTerm)
    {
        updatedValueTerm = null!;
        valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression);
        if (previousValueTerm.Kind != SmtValueKind.Int) return false;

        var substitutions = new Dictionary<ISymbol, SymbolicTerm>(SymbolEqualityComparer.Default)
        {
            [assignedSymbol.OriginalDefinition] = previousValueTerm
        };
        var lowering = SymbolicSemanticPipeline.LowerTerm(
            valueExpression,
            new SymbolicLoweringContext(
                semanticModel,
                cancellationToken,
                symbolSubstitutions: substitutions));
        if (lowering is not { IsExact: true, Value: { Kind: SmtValueKind.Int } updatedValue })
            return false;

        updatedValueTerm = updatedValue;
        return true;
    }

    private static void AddMemberNonNullStateFact(
        ref SymbolicState state,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance)
    {
        if (target.Kind == SmtValueKind.Reference &&
            NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                valueExpression,
                semanticModel,
                cancellationToken))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm(),
                valueExpression,
                provenance + ".assigned-non-null");
    }

    internal static bool TryHandleTupleAssignmentState(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(assignment.Left) is not
                (TupleExpressionSyntax or DeclarationExpressionSyntax))
            return false;

        if (semanticModel.GetOperation(assignment, cancellationToken) is not IDeconstructionAssignmentOperation operation ||
            !SymbolicDeconstructionPlan.TryCollectTargets(
                operation.Target,
                target => ResolveDeconstructionTarget(target, semanticModel, cancellationToken),
                out var targets))
            return true;

        var targetSymbols = targets.Select(static target => target.Symbol).ToArray();

        foreach (var targetSymbol in targetSymbols)
            if (targetSymbol != null)
                state = SymbolicStateValueFacts.RemoveReferences(state, targetSymbol);

        var nonDiscardTargets = targetSymbols.Where(static symbol => symbol != null).Cast<ISymbol>().ToArray();
        if (targetSymbols.All(static symbol => symbol == null) ||
            ExpressionReferencesAnySymbol(assignment.Right, nonDiscardTargets, semanticModel, cancellationToken))
            return true;

        AddTupleElementTargetStateFacts(
            ref state,
            targetSymbols,
            assignment.Right,
            semanticModel,
            cancellationToken,
            "ir.path.prior-statement.tuple-target");
        return true;
    }

    private static ISymbol? ResolveDeconstructionTarget(
        IOperation operation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ when operation.Syntax is SingleVariableDesignationSyntax designation =>
                semanticModel.GetDeclaredSymbol(designation, cancellationToken),
            _ when operation.Syntax is IdentifierNameSyntax identifier =>
                semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
            _ => null
        };
    }

    private static void AddTupleElementTargetStateFacts(
        ref SymbolicState state,
        IReadOnlyList<ISymbol?> targetSymbols,
        ExpressionSyntax rightExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        rightExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(rightExpression);
        if (rightExpression is TupleExpressionSyntax rightTuple)
        {
            if (rightTuple.Arguments.Count != targetSymbols.Count) return;

            for (var index = 0; index < targetSymbols.Count; index++)
            {
                if (targetSymbols[index] == null) continue;

                AddAssignedValueStateFacts(
                    ref state,
                    targetSymbols[index]!,
                    rightTuple.Arguments[index].Expression,
                    semanticModel,
                    cancellationToken,
                    provenanceRoot);
            }

            return;
        }

        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                rightExpression,
                semanticModel,
                cancellationToken,
                out var sourceSymbol) ||
            !SymbolicOperationLowerer.TryGetTupleElementStorageNames(
                sourceSymbol,
                targetSymbols.Count,
                out var sourceElementNames))
            return;

        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(targetSymbols.Count);
        for (var index = 0; index < targetSymbols.Count; index++)
        {
            if (targetSymbols[index] == null ||
                !TryCreateSymbolTerm(targetSymbols[index]!, out var targetTerm) ||
                !SymbolicOperationLowerer.TryCreateTupleElementTerm(
                    sourceSymbol,
                    sourceElementNames[index],
                    new SymbolicLoweringContext(semanticModel, cancellationToken),
                    out var sourceElementTerm) ||
                !CanCompareIrTerms(targetTerm, sourceElementTerm))
                continue;

            bindings.Add(new SymbolicAssignmentBinding(
                SymbolicFactFactory.GetSmtVariableName(targetSymbols[index]!),
                targetTerm,
                sourceElementTerm,
                provenanceRoot + ".assigned-value",
                PropagateSourceFacts: true));
        }

        if (bindings.Count == 0) return;
        var transition = SymbolicOperationTransferAdapter.ApplyBindings(
            state,
            bindings.ToImmutable(),
            rightExpression,
            SymbolicAssignmentOperationKind.Deconstruction,
            provenanceRoot);
        if (!transition.IsExact) return;

        state = transition.State;
    }

    private static void AddSwitchExpressionAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression) is not SwitchExpressionSyntax switchExpression ||
            switchExpression.Arms.Count == 0 ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetTerm))
            return;

        var conditionSymbols = SymbolicBranchCompletionStateTransfer.GetSwitchExpressionConditionSymbols(
            switchExpression,
            semanticModel,
            cancellationToken);
        if (SymbolicLoopStateTransfer.ExpressionMutatesAnySymbol(
                switchExpression,
                conditionSymbols,
                semanticModel,
                cancellationToken))
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var addedCount = 0;
        foreach (var arm in switchExpression.Arms)
        {
            if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
                continue;

            SymbolicCondition? armFact = null;
            if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(arm.Expression) is ThrowExpressionSyntax)
            {
                armFact = new SymbolicNotCondition(armCondition);
            }
            else if (SymbolicSemanticPipeline.LowerTerm(arm.Expression, context) is
                     { IsExact: true, Value: { } armValueTerm } &&
                     armValueTerm.Kind == targetTerm.Kind &&
                     CanCompareIrTerms(targetTerm, armValueTerm))
            {
                armFact = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    new SymbolicNotCondition(armCondition),
                    new SymbolicFactCondition(SymbolicFact.Exact(
                        new SymbolicRelationAtom(SymbolicRelationOperator.Equal, targetTerm, armValueTerm),
                        arm.Expression,
                        provenanceRoot + ".switch-expression-assigned-value",
                        assignedSymbol)));
            }

            if (armFact == null) continue;

            if (!SymbolicAnalysisLimitContext.CanAddMergedSwitchFact(
                    addedCount,
                    switchExpression,
                    "program_point.switch_expression_state_fact_merge"))
                return;

            state = state.AddPathCondition(armFact);
            addedCount++;
        }
    }

    internal static void AddElementAssignmentStateFact(
        ref SymbolicState state,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(assignment.Left) is not ElementAccessExpressionSyntax elementAccess)
            return;

        var receiverSymbols = SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
            elementAccess.Expression,
            semanticModel,
            cancellationToken);
        if (ExpressionReferencesAnySymbol(assignment.Right, receiverSymbols, semanticModel, cancellationToken))
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var targetLowering = SymbolicSemanticPipeline.LowerTerm(elementAccess, context);
        var valueLowering = SymbolicSemanticPipeline.LowerTerm(assignment.Right, context);
        if (targetLowering is not { IsExact: true, Value: { } target } ||
            valueLowering is not { IsExact: true, Value: { } value } ||
            !CanCompareIrTerms(target, value))
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            target,
            value,
            assignment,
            "ir.path.prior-statement.element-assignment");
    }

    internal static SymbolicThrowGuardedValue GetThrowGuardedValue(ExpressionSyntax valueExpression)
    {
        var originalValueExpression = valueExpression;
        valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(originalValueExpression);
        if (valueExpression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(coalesceExpression.Right) is ThrowExpressionSyntax)
            return new SymbolicThrowGuardedValue(
                true,
                coalesceExpression.Left,
                null,
                true,
                true);

        if (valueExpression is ConditionalExpressionSyntax conditionalExpression)
        {
            if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(conditionalExpression.WhenFalse) is ThrowExpressionSyntax)
                return new SymbolicThrowGuardedValue(
                    true,
                    conditionalExpression.WhenTrue,
                    conditionalExpression.Condition,
                    true,
                    false);

            if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(conditionalExpression.WhenTrue) is ThrowExpressionSyntax)
                return new SymbolicThrowGuardedValue(
                    true,
                    conditionalExpression.WhenFalse,
                    conditionalExpression.Condition,
                    false,
                    false);
        }

        return new SymbolicThrowGuardedValue(false, originalValueExpression, null, true, false);
    }

    internal static bool TryCreateMemberDerivedTerm(
        SymbolicTerm receiver,
        ISymbol memberSymbol,
        SmtValueKind kind,
        out SymbolicTerm output)
    {
        output = null!;
        if (receiver.Kind != SmtValueKind.Reference) return false;

        output = new SymbolicMemberTerm(receiver, memberSymbol.Name, kind);
        return true;
    }

    internal static bool ExpressionReferencesAnySymbol(
        SyntaxNode root,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in symbols)
            if (SymbolMutationFacts.ExpressionReferencesSymbol(root, symbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

}
