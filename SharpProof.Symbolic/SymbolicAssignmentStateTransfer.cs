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
            AddAssignedSourceSymbolSnapshotStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken);

        SymbolicTerm? assignedValueTerm = null;
        if (isSelfReferential)
            assignedValueTerm = selfReferentialValueTerm;
        else
        {
            if (assignedType?.SpecialType == SpecialType.System_Boolean &&
                SymbolicSemanticPipeline.LowerBooleanValueTerm(effectiveValueExpression, context) is
                { IsExact: true, Value: { } loweredBooleanValueTerm })
                assignedValueTerm = loweredBooleanValueTerm;

            if (assignedValueTerm == null &&
                SymbolicSemanticPipeline.LowerTerm(effectiveValueExpression, context) is
                { IsExact: true, Value: { } loweredValueTerm })
                assignedValueTerm = loweredValueTerm;
        }

        if (!isSelfReferential)
            AddSwitchExpressionAssignedValueStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken,
                provenanceRoot);

        if (!isSelfReferential)
            AddAssignedNullableStateFacts(
                ref state,
                assignedSymbol,
                effectiveValueExpression,
                context,
                provenanceRoot);

        if (isSelfReferential &&
                 TryCreateSymbolTerm(assignedSymbol, out var targetTerm) &&
                 assignedValueTerm != null &&
                 assignedValueTerm.Kind == targetTerm.Kind &&
                 CanCompareIrTerms(targetTerm, assignedValueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                assignedValueTerm,
                effectiveValueExpression,
                provenanceRoot + ".assigned-value");

        AddNotNullIfNotNullAssignedStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            context,
            provenanceRoot);
        AddAssignedIntegerRangeStateFacts(
            ref state,
            assignedSymbol,
            assignedValueTerm,
            effectiveValueExpression,
            provenanceRoot);
        if (isSelfReferential &&
            TryCreateSymbolTerm(assignedSymbol, out var selfReferenceTarget))
            foreach (var condition in SymbolicOperationLowerer.LowerSymbolicReferenceBackedPostconditions(
                         selfReferenceTarget,
                         effectiveValueExpression,
                         context,
                         provenanceRoot))
                state = state.AddPathCondition(condition);
        AddFiniteArrayElementAssignedValueStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
        AddRemainderAssignedRangeStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            context,
            provenanceRoot);
        AddTupleElementAssignedValueStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);
        AddTupleElementSourceSymbolSnapshotStateFacts(
            ref state,
            assignedSymbol,
            effectiveValueExpression,
            semanticModel,
            cancellationToken,
            provenanceRoot);

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

    private static void AddAssignedSourceSymbolSnapshotStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                semanticModel,
                cancellationToken,
                out var sourceSymbol) ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, assignedSymbol))
            return;

        if (TryCreateSymbolTerm(sourceSymbol, out var sourceTerm) &&
            TryCreateSymbolTerm(assignedSymbol, out var targetTerm) &&
            CanCompareIrTerms(sourceTerm, targetTerm))
            AddSubstitutedStateFacts(ref state, sourceTerm, targetTerm);

        if (TryCreateNullableSymbolTerms(sourceSymbol, out var sourceHasValue, out var sourceValue) &&
            TryCreateNullableSymbolTerms(assignedSymbol, out var targetHasValue, out var targetValue) &&
            CanCompareIrTerms(sourceHasValue, targetHasValue) &&
            CanCompareIrTerms(sourceValue, targetValue))
        {
            AddSubstitutedStateFacts(ref state, sourceHasValue, targetHasValue);
            AddSubstitutedStateFacts(ref state, sourceValue, targetValue);
        }
    }

    private static void AddSubstitutedStateFacts(
        ref SymbolicState state,
        SymbolicTerm source,
        SymbolicTerm target)
    {
        if (!CanCompareIrTerms(source, target) ||
            string.Equals(
                SymbolicState.CreateProofTermKey(source),
                SymbolicState.CreateProofTermKey(target),
                StringComparison.Ordinal))
            return;

        var existingFacts = state.Facts;
        var existingConditions = state.PathConditions;
        foreach (var fact in existingFacts)
        {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(fact, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofFactKey(substituted),
                    SymbolicState.CreateProofFactKey(fact),
                    StringComparison.Ordinal))
                state = state.AddFact(substituted);
        }

        foreach (var condition in existingConditions)
        {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(condition, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofConditionKey(substituted),
                    SymbolicState.CreateProofConditionKey(condition),
                    StringComparison.Ordinal))
                state = state.AddPathCondition(substituted);
        }
    }

    private static void AddAssignedNullableStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (!TryCreateNullableSymbolTerms(
                assignedSymbol,
                out var targetHasValue,
                out var targetValue))
            return;

        SymbolicTerm sourceHasValue;
        SymbolicTerm? sourceValue = null;
        var hasValueLowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(valueExpression, context);
        if (hasValueLowering is { IsExact: true, Value: { } nullableHasValue })
        {
            sourceHasValue = nullableHasValue;
            if (SymbolicSemanticPipeline.LowerNullableValueTerm(valueExpression, context) is
                { IsExact: true, Value: { } nullableValue })
                sourceValue = nullableValue;
        }
        else if (SymbolicSemanticPipeline.LowerTerm(valueExpression, context) is
                 { IsExact: true, Value: { } wrappedValue } &&
                 wrappedValue.Kind == targetValue.Kind)
        {
            sourceHasValue = new SymbolicBooleanConstantTerm(true);
            sourceValue = wrappedValue;
        }
        else
        {
            return;
        }

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetHasValue,
            sourceHasValue,
            valueExpression,
            provenanceRoot + ".nullable.has-value");

        if (sourceValue == null ||
            !CanCompareIrTerms(targetValue, sourceValue))
            return;

        var targetHasValueFact = SymbolicFact.Exact(
            new SymbolicTruthAtom(targetHasValue),
            valueExpression,
            provenanceRoot + ".nullable.value-present",
            assignedSymbol);
        var targetValueFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.Equal, targetValue, sourceValue),
            valueExpression,
            provenanceRoot + ".nullable.value",
            assignedSymbol);
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(new SymbolicFactCondition(targetHasValueFact)),
            new SymbolicFactCondition(targetValueFact)));
    }

    internal static bool TryCreateNullableSymbolTerms(
        ISymbol symbol,
        out SymbolicTerm hasValue,
        out SymbolicTerm value)
    {
        hasValue = null!;
        value = null!;
        if (!TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(symbol), out var underlyingType) ||
            !TryGetValueKind(underlyingType, out var valueKind))
            return false;

        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol);
        hasValue = new SymbolicNullableHasValueTerm(symbolName);
        value = new SymbolicNullableValueTerm(symbolName, valueKind);
        return true;
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

    private static void AddFiniteArrayElementAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
            !TryGetValueKind(arrayType.ElementType, out var elementKind) ||
            !SymbolicProgramPointFacts.TryGetFiniteElementExpressions(
                valueExpression,
                out var elementExpressions) ||
            !TryCreateSymbolTerm(assignedSymbol, out var receiver) ||
            receiver.Kind != SmtValueKind.Reference)
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        for (var index = 0; index < elementExpressions.Length; index++)
        {
            var elementExpression = elementExpressions[index];
            var lowering = SymbolicSemanticPipeline.LowerTerm(elementExpression, context);
            if (SymbolMutationFacts.ExpressionReferencesSymbol(
                    elementExpression,
                    assignedSymbol,
                    semanticModel,
                    cancellationToken) ||
                lowering is not { IsExact: true, Value: { } elementValue } ||
                elementValue.Kind != elementKind)
                continue;

            var targetElement = new SymbolicElementTerm(
                receiver,
                new SymbolicIntegerConstantTerm(index),
                elementKind);
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetElement,
                elementValue,
                elementExpression,
                provenanceRoot + ".finite-array-element");

            var targetFromEndElement = new SymbolicElementTerm(
                receiver,
                new SymbolicFromEndIndexTerm(
                    new SymbolicIntegerConstantTerm(elementExpressions.Length - index)),
                elementKind);
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetFromEndElement,
                elementValue,
                elementExpression,
                provenanceRoot + ".finite-array-element.from-end");

            if (elementKind == SmtValueKind.Reference &&
                NullableFlowFacts.IsDefinitelyNotNullReferenceValue(elementExpression, semanticModel, cancellationToken))
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.NotEqual,
                    targetElement,
                    new SymbolicNullTerm(),
                    elementExpression,
                    provenanceRoot + ".finite-array-element.non-null");
        }
    }

    private static void AddAssignedIntegerRangeStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        SymbolicTerm? assignedValueTerm,
        ExpressionSyntax valueExpression,
        string provenanceRoot)
    {
        if (assignedValueTerm is not { Kind: SmtValueKind.Int } valueTerm ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetTerm) ||
            targetTerm.Kind != SmtValueKind.Int)
            return;

        if (StateProvesPositiveInteger(state, valueTerm))
        {
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThan,
                targetTerm,
                new SymbolicIntegerConstantTerm(0),
                valueExpression,
                provenanceRoot + ".assigned-integer.positive");
            return;
        }

        if (StateProvesNonNegativeInteger(state, valueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.GreaterThanOrEqual,
                targetTerm,
                new SymbolicIntegerConstantTerm(0),
                valueExpression,
                provenanceRoot + ".assigned-integer.non-negative");
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

    private static void AddNotNullIfNotNullAssignedStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (!TryCreateSymbolTerm(assignedSymbol, out var target) ||
            target.Kind != SmtValueKind.Reference ||
            SymbolicSemanticPipeline.LowerNotNullIfNotNullAssignedResultTerm(valueExpression, context) is not
                { IsExact: true, Value: { Kind: SmtValueKind.Bool } resultNonNull })
            return;

        var targetNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm()),
            valueExpression,
            provenanceRoot + ".not-null-if-not-null.target",
            assignedSymbol));
        var targetNonNullTerm = new SymbolicConditionalTerm(
            targetNonNull,
            new SymbolicBooleanConstantTerm(true),
            new SymbolicBooleanConstantTerm(false));
        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.Equal,
            targetNonNullTerm,
            resultNonNull,
            valueExpression,
            provenanceRoot + ".not-null-if-not-null.result");
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

    private static void AddRemainderAssignedRangeStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression);
        if (valueExpression is not BinaryExpressionSyntax moduloExpression ||
            !moduloExpression.IsKind(SyntaxKind.ModuloExpression) ||
            !TryCreateSymbolTerm(assignedSymbol, out var targetTerm) ||
            targetTerm.Kind != SmtValueKind.Int)
            return;

        var dividendLowering = SymbolicSemanticPipeline.LowerTerm(moduloExpression.Left, context);
        var divisorLowering = SymbolicSemanticPipeline.LowerTerm(moduloExpression.Right, context);
        if (dividendLowering is not { IsExact: true, Value: { } dividendTerm } ||
            dividendTerm.Kind != SmtValueKind.Int ||
            divisorLowering is not { IsExact: true, Value: { } divisorTerm } ||
            divisorTerm.Kind != SmtValueKind.Int ||
            !StateProvesNonNegativeInteger(state, dividendTerm) ||
            !StateProvesPositiveInteger(state, divisorTerm))
            return;

        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.GreaterThanOrEqual,
            targetTerm,
            new SymbolicIntegerConstantTerm(0),
            valueExpression,
            provenanceRoot + ".assigned-remainder.non-negative");
        AddRelationPathFact(
            ref state,
            SymbolicRelationOperator.LessThan,
            targetTerm,
            divisorTerm,
            valueExpression,
            provenanceRoot + ".assigned-remainder.upper-bound");
    }

    private static bool StateProvesPositiveInteger(SymbolicState state, SymbolicTerm term)
    {
        return StateConditionProvesIntegerRelation(state, term, true);
    }

    private static bool StateProvesNonNegativeInteger(SymbolicState state, SymbolicTerm term)
    {
        return StateConditionProvesIntegerRelation(state, term, false);
    }

    private static bool StateConditionProvesIntegerRelation(
        SymbolicState state,
        SymbolicTerm term,
        bool requireStrictlyPositive)
    {
        if (term.Kind != SmtValueKind.Int) return false;

        return state.PathConditions.Any(condition =>
                   ConditionProvesIntegerRelation(condition, term, requireStrictlyPositive)) ||
               state.Facts.Any(fact => FactProvesIntegerRelation(fact, term, requireStrictlyPositive));
    }

    private static bool ConditionProvesIntegerRelation(
        SymbolicCondition condition,
        SymbolicTerm term,
        bool requireStrictlyPositive)
    {
        return condition switch
        {
            SymbolicFactCondition factCondition => FactProvesIntegerRelation(factCondition.Fact, term,
                requireStrictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binaryCondition =>
                ConditionProvesIntegerRelation(binaryCondition.Left, term, requireStrictlyPositive) ||
                ConditionProvesIntegerRelation(binaryCondition.Right, term, requireStrictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binaryCondition =>
                ConditionProvesIntegerRelation(binaryCondition.Left, term, requireStrictlyPositive) &&
                ConditionProvesIntegerRelation(binaryCondition.Right, term, requireStrictlyPositive),
            _ => false
        };
    }

    private static bool FactProvesIntegerRelation(
        SymbolicFact fact,
        SymbolicTerm term,
        bool requireStrictlyPositive)
    {
        if (!fact.Polarity ||
            fact.Atom is not SymbolicRelationAtom relation)
            return false;

        if (Equals(relation.Left, term) &&
            relation.Right is SymbolicIntegerConstantTerm rightConstant)
            return RelationProvesIntegerRelation(
                relation.Operator,
                rightConstant.Value,
                requireStrictlyPositive,
                true);

        if (Equals(relation.Right, term) &&
            relation.Left is SymbolicIntegerConstantTerm leftConstant)
            return RelationProvesIntegerRelation(
                relation.Operator,
                leftConstant.Value,
                requireStrictlyPositive,
                false);

        return false;
    }

    private static bool RelationProvesIntegerRelation(
        SymbolicRelationOperator op,
        long constant,
        bool requireStrictlyPositive,
        bool termOnLeft)
    {
        return (termOnLeft, op) switch
        {
            (true, SymbolicRelationOperator.GreaterThan) => requireStrictlyPositive ? constant >= 0 : constant >= -1,
            (true, SymbolicRelationOperator.GreaterThanOrEqual) => requireStrictlyPositive
                ? constant > 0
                : constant >= 0,
            (true, SymbolicRelationOperator.Equal) => requireStrictlyPositive ? constant > 0 : constant >= 0,
            (false, SymbolicRelationOperator.LessThan) => requireStrictlyPositive ? constant <= 0 : constant <= -1,
            (false, SymbolicRelationOperator.LessThanOrEqual) => requireStrictlyPositive ? constant < 0 : constant <= 0,
            (false, SymbolicRelationOperator.Equal) => requireStrictlyPositive ? constant > 0 : constant >= 0,
            _ => false
        };
    }

    private static void AddTupleElementAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression);
        if (valueExpression is not TupleExpressionSyntax tupleExpression ||
            !TryGetTupleElementStorageNames(assignedSymbol, tupleExpression.Arguments.Count, out var elementNames))
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        for (var index = 0; index < tupleExpression.Arguments.Count; index++)
        {
            var argumentExpression = tupleExpression.Arguments[index].Expression;
            if (SymbolMutationFacts.ExpressionReferencesSymbol(argumentExpression, assignedSymbol, semanticModel, cancellationToken))
                continue;

            AddTupleElementAssignedValueStateFacts(
                ref state,
                assignedSymbol,
                elementNames[index],
                argumentExpression,
                semanticModel,
                cancellationToken,
                context,
                provenanceRoot + ".tuple-element");
        }
    }

    private static void AddTupleElementAssignedValueStateFacts(
        ref SymbolicState state,
        ISymbol tupleSymbol,
        string elementName,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicLoweringContext context,
        string provenanceRoot)
    {
        if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
            !TryCreateTupleElementTerm(tupleSymbol, elementName, out var targetTerm))
            return;

        if (elementType.SpecialType == SpecialType.System_String)
        {
            if (SymbolicSemanticPipeline.ProjectStringContentTerm(targetTerm, valueExpression) is
                    { IsExact: true, Value: { } targetString } &&
                SymbolicSemanticPipeline.LowerStringTerm(valueExpression, context) is
                    { IsExact: true, Value: { } valueString })
                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.Equal,
                    targetString,
                    valueString,
                    valueExpression,
                    provenanceRoot + ".assigned-string");
        }
        else if (SymbolicSemanticPipeline.LowerTerm(valueExpression, context) is
                 { IsExact: true, Value: { } valueTerm } &&
                 CanCompareIrTerms(targetTerm, valueTerm))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetTerm,
                valueTerm,
                valueExpression,
                provenanceRoot + ".assigned-value");

        if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(valueExpression, semanticModel, cancellationToken) &&
            targetTerm.Kind == SmtValueKind.Reference)
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.NotEqual,
                targetTerm,
                new SymbolicNullTerm(),
                valueExpression,
                provenanceRoot + ".assigned-non-null");

        if (SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(elementType, targetTerm, valueExpression) is
                { IsExact: true, Value: { } targetLength } &&
            SymbolicSemanticPipeline.LowerBuiltInLengthTerm(valueExpression, context) is
                { IsExact: true, Value: { } valueLength } &&
            CanCompareIrTerms(targetLength, valueLength))
            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetLength,
                valueLength,
                valueExpression,
                provenanceRoot + ".assigned-length");

        if (targetTerm.Kind == SmtValueKind.Reference &&
            elementType is IArrayTypeSymbol { Rank: > 1 } arrayType)
            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
                if (SymbolicSemanticPipeline.LowerArrayDimensionLengthTerm(
                        valueExpression,
                        dimension,
                        context) is { IsExact: true, Value: { } dimensionLength })
                    AddRelationPathFact(
                        ref state,
                        SymbolicRelationOperator.Equal,
                        new SymbolicArrayDimensionLengthTerm(targetTerm, dimension),
                        dimensionLength,
                        valueExpression,
                        provenanceRoot + ".assigned-dimension-length");
    }

    private static void AddTupleElementSourceSymbolSnapshotStateFacts(
        ref SymbolicState state,
        ISymbol assignedSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenanceRoot)
    {
        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                semanticModel,
                cancellationToken,
                out var sourceSymbol) ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, assignedSymbol) ||
            !TryGetTupleElementStorageNames(assignedSymbol, 0, out var targetElementNames) ||
            !TryGetTupleElementStorageNames(sourceSymbol, targetElementNames.Length, out var sourceElementNames))
            return;

        for (var index = 0; index < targetElementNames.Length; index++)
        {
            if (!TryCreateTupleElementTerm(assignedSymbol, targetElementNames[index], out var targetElement) ||
                !TryCreateTupleElementTerm(sourceSymbol, sourceElementNames[index], out var sourceElement) ||
                !CanCompareIrTerms(targetElement, sourceElement))
                continue;

            AddRelationPathFact(
                ref state,
                SymbolicRelationOperator.Equal,
                targetElement,
                sourceElement,
                valueExpression,
                provenanceRoot + ".tuple-element.snapshot");
        }
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
            !TryGetTupleElementStorageNames(sourceSymbol, targetSymbols.Count, out var sourceElementNames))
            return;

        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(targetSymbols.Count);
        var substitutions = new List<(SymbolicTerm Source, SymbolicTerm Target)>();
        for (var index = 0; index < targetSymbols.Count; index++)
        {
            if (targetSymbols[index] == null ||
                !TryCreateSymbolTerm(targetSymbols[index]!, out var targetTerm) ||
                !TryCreateTupleElementTerm(sourceSymbol, sourceElementNames[index], out var sourceElementTerm) ||
                !CanCompareIrTerms(targetTerm, sourceElementTerm))
                continue;

            bindings.Add(new SymbolicAssignmentBinding(
                SymbolicFactFactory.GetSmtVariableName(targetSymbols[index]!),
                targetTerm,
                sourceElementTerm,
                provenanceRoot + ".assigned-value"));
            substitutions.Add((sourceElementTerm, targetTerm));
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
        foreach (var substitution in substitutions)
            AddSubstitutedStateFacts(ref state, substitution.Source, substitution.Target);
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

    private static bool TryGetTupleElementStorageNames(
        ISymbol assignedSymbol,
        int expectedCount,
        out string[] elementNames)
    {
        elementNames = Array.Empty<string>();
        var type = assignedSymbol switch
        {
            ILocalSymbol localSymbol => localSymbol.Type,
            IParameterSymbol parameterSymbol => parameterSymbol.Type,
            _ => null
        };

        if (type is not INamedTypeSymbol { IsTupleType: true } tupleType ||
            (expectedCount > 0 &&
             tupleType.TupleElements.Length != expectedCount))
            return false;

        elementNames = new string[tupleType.TupleElements.Length];
        for (var index = 0; index < tupleType.TupleElements.Length; index++)
        {
            var field = tupleType.TupleElements[index].CorrespondingTupleField ?? tupleType.TupleElements[index];
            if (string.IsNullOrWhiteSpace(field.Name)) return false;

            elementNames[index] = field.Name;
        }

        return true;
    }

    private static bool TryGetTupleElementType(
        ISymbol tupleSymbol,
        string elementName,
        out ITypeSymbol elementType)
    {
        var type = SymbolicFactFactory.GetTrackedSymbolType(tupleSymbol);
        if (type is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            elementType = null!;
            return false;
        }

        var element = tupleType.TupleElements
            .FirstOrDefault(field =>
                string.Equals((field.CorrespondingTupleField ?? field).Name, elementName, StringComparison.Ordinal));
        if (element == null)
        {
            elementType = null!;
            return false;
        }

        elementType = element.Type;
        return true;
    }

    private static bool TryCreateTupleElementTerm(
        ISymbol tupleSymbol,
        string elementName,
        out SymbolicTerm term)
    {
        if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
            !TryGetValueKind(elementType, out var elementKind))
        {
            term = null!;
            return false;
        }

        var tuple = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(tupleSymbol),
            SmtValueKind.Reference);
        term = new SymbolicMemberTerm(tuple, elementName, elementKind);
        return true;
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

    private static bool TryGetNullableUnderlyingType(ITypeSymbol? type, out ITypeSymbol underlyingType)
    {
        return SymbolicTypeFacts.TryGetNullableUnderlyingType(type, out underlyingType);
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
