using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicTrackedAssignmentStateTransfer
{
    internal static void AddFacts(
        ref SymbolicState state,
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int> getTargetVersion,
        Func<ISymbol, int> getValueVersion,
        string provenanceRoot)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetContext = new SymbolicLoweringContext(semanticModel, cancellationToken, getTargetVersion);
        var valueContext = new SymbolicLoweringContext(semanticModel, cancellationToken, getValueVersion);
        if (TryCreateSymbolTerm(targetSymbol, targetContext, out var target))
            AddEquality(
                ref state,
                target,
                valueExpression,
                valueContext,
                SymbolicSemanticPipeline.LowerTerm,
                provenanceRoot,
                provenanceRoot + ".value");

        if (TryCreateSymbolTerm(targetSymbol, targetContext, out target) && target.Kind == SmtValueKind.Reference)
        {
            AddEquality(
                ref state,
                new SymbolicLengthTerm(target),
                valueExpression,
                valueContext,
                SymbolicSemanticPipeline.LowerLengthProjectionTerm,
                provenanceRoot + ".length",
                provenanceRoot + ".length");
            AddCollectionLengthLowerBound(
                ref state,
                target,
                valueExpression,
                provenanceRoot + ".collection_length");

            if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.SpecialType == SpecialType.System_String)
            {
                AddEquality(
                    ref state,
                    new SymbolicStringContentTerm(target),
                    valueExpression,
                    valueContext,
                    SymbolicSemanticPipeline.LowerStringTerm,
                    provenanceRoot + ".string",
                    provenanceRoot + ".string");
                AddStringNullEquivalence(
                    ref state,
                    target,
                    valueExpression,
                    valueContext,
                    provenanceRoot + ".string_nonnull");
            }
        }

        var asExpressionFacts = SymbolicSemanticPipeline.LowerAsExpressionAssignmentFacts(
            targetSymbol,
            valueExpression,
            valueContext,
            getTargetVersion);
        if (asExpressionFacts is { IsExact: true, Value: { } asExpressionState })
            foreach (var condition in asExpressionState.PathConditions)
                state = state.AddPathCondition(condition);
    }

    private static void AddEquality(
        ref SymbolicState state,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext valueContext,
        Func<ExpressionSyntax, SymbolicLoweringContext, SymbolicLoweringResult<SymbolicTerm>> lowerValue,
        string provenance,
        string evidenceKey)
    {
        if (lowerValue(valueExpression, valueContext) is not { IsExact: true, Value: { } value } ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(target, value))
            return;

        state = state.AddPathCondition(new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.Equal, target, value),
            valueExpression,
            provenance,
            evidenceKey: evidenceKey)));
    }

    private static bool TryCreateSymbolTerm(
        ISymbol symbol,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);
        if (type == null ||
            !SymbolicFactFactory.TryGetValueKind(
                type,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsReferenceType,
                out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
        return true;
    }

    private static void AddCollectionLengthLowerBound(
        ref SymbolicState state,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        string provenance)
    {
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression) is not
                CollectionExpressionSyntax collectionExpression)
            return;

        var lowerBound = collectionExpression.Elements.Count(static element => element is ExpressionElementSyntax);
        if (lowerBound == 0 || !collectionExpression.Elements.Any(static element => element is SpreadElementSyntax))
            return;

        state = state.AddPathCondition(new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThanOrEqual,
                new SymbolicLengthTerm(target),
                new SymbolicIntegerConstantTerm(lowerBound)),
            valueExpression,
            provenance,
            evidenceKey: provenance)));
    }

    private static void AddStringNullEquivalence(
        ref SymbolicState state,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext valueContext,
        string provenance)
    {
        if (SymbolicSemanticPipeline.LowerStringNonNullCondition(valueExpression, valueContext) is not
            { IsExact: true, Value: { } valueNonNull })
            return;

        var targetNonNull = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm()),
            valueExpression,
            provenance,
            evidenceKey: provenance));
        state = state.AddPathCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, targetNonNull, valueNonNull),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicNotCondition(targetNonNull),
                new SymbolicNotCondition(valueNonNull))));
    }
}
