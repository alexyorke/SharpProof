using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic.Ir;

internal sealed record SymbolicSourceCompletionPlan(
    ImmutableArray<SymbolicCondition> Conditions);

internal static class SymbolicSourceCompletionLowerer
{
    internal static SymbolicOperationTransitionResult ApplyThrowGuard(
        SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax guardedStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string nonNullProvenance)
    {
        var guardedValue = SymbolicAssignmentStateTransfer.GetThrowGuardedValue(expression);
        if (!guardedValue.HasGuard)
            return Exact(state, expression, "no-throw-guard");
        if (guardedValue.GuardExpression is { } guard)
            return SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                guard,
                guardedStatement,
                semanticModel,
                cancellationToken)
                ? Exact(state, guard, "invalidated-guard")
                : SymbolicReachabilityLowerer.Apply(
                    state,
                    guard,
                    guardedValue.GuardBranchWhenTrue,
                    semanticModel,
                    cancellationToken);
        if (!guardedValue.RequiresNonNullValue ||
            SymbolicLoopStateTransfer.ReferenceIdentityFactIsInvalidatedInStatement(
                guardedValue.EffectiveValueExpression,
                guardedStatement,
                semanticModel,
                cancellationToken))
            return Exact(state, expression, "no-stable-reference");
        if (NullableFlowFacts.IsDefinitelyNullReferenceValue(
                guardedValue.EffectiveValueExpression,
                semanticModel,
                cancellationToken))
            return SymbolicOperationTransferKernel.Complete(
                state,
                guardedValue.EffectiveValueExpression.Span);
        if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                guardedValue.EffectiveValueExpression,
                semanticModel,
                cancellationToken))
            return Exact(state, expression, "known-non-null");

        var lowering = SymbolicSemanticPipeline.LowerTerm(
            guardedValue.EffectiveValueExpression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { Kind: SmtValueKind.Reference } subject })
            return Unsupported(state, expression, "reference");
        return SymbolicOperationTransferKernel.Assume(
            state,
            SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                subject,
                new SymbolicNullTerm(),
                guardedValue.EffectiveValueExpression,
                nonNullProvenance),
            assumeTrue: true,
            guardedValue.EffectiveValueExpression.Span,
            nonNullProvenance);
    }

    internal static SymbolicLoweringResult<SymbolicSourceCompletionPlan> Lower(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        AddArrayBounds(conditions, expression, statement, semanticModel, cancellationToken);
        AddDereferenceSuccess(conditions, expression, statement, semanticModel, cancellationToken);
        return SymbolicLoweringResult<SymbolicSourceCompletionPlan>.Exact(
            new SymbolicSourceCompletionPlan(conditions.ToImmutable()),
            new SymbolicLoweringProvenance("source-completion", expression.Span, "exact"));
    }

    private static void AddArrayBounds(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = SymbolicFrameworkPostconditionLowerer.UnwrapAwaited(expression);
        if (expression is not ArrayCreationExpressionSyntax arrayCreation)
            return;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        foreach (var sizeExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
            if (!SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                    sizeExpression,
                    statement,
                    semanticModel,
                    cancellationToken) &&
                SymbolicSemanticPipeline.LowerTerm(sizeExpression, context) is
                    { IsExact: true, Value: { Kind: SmtValueKind.Int } size })
                conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    size,
                    new SymbolicIntegerConstantTerm(0),
                    sizeExpression,
                    "ir.path.normal-completion.array-length.non-negative"));
    }

    private static void AddDereferenceSuccess(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is AwaitExpressionSyntax awaited)
        {
            var awaitable = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(awaited.Expression);
            AddStableNonNull(
                conditions,
                awaitable,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.awaitable-not-null");
            expression = awaitable;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess &&
            elementAccess.ArgumentList.Arguments.Count == 1 &&
            !SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                elementAccess,
                statement,
                semanticModel,
                cancellationToken) &&
            SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(
                elementAccess,
                new SymbolicLoweringContext(semanticModel, cancellationToken)) is
                { IsExact: true, Value: { } inRange })
            conditions.Add(inRange);

        if (TryGetDereferenceReceiver(expression, semanticModel, cancellationToken, out var receiver))
            AddStableNonNull(
                conditions,
                receiver,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.normal-completion.dereference.receiver-not-null");
    }

    private static void AddStableNonNull(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance)
    {
        if (NullableFlowFacts.TryGetArgumentTargetSymbol(
                expression,
                semanticModel,
                cancellationToken,
                out var symbol) &&
            !SymbolicLoopStateTransfer.AnyConditionSymbolMutatedInStatement(
                expression,
                statement,
                semanticModel,
                cancellationToken) &&
            TryCreateSymbolTerm(symbol, out var term) &&
            term.Kind == SmtValueKind.Reference)
            conditions.Add(SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                term,
                new SymbolicNullTerm(),
                expression,
                provenance));
    }

    private static bool TryGetDereferenceReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax receiver)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        switch (expression)
        {
            case InvocationExpressionSyntax invocation
                when CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(invocation.Expression) is
                         MemberAccessExpressionSyntax memberAccess &&
                     !IsReducedExtensionMethodInvocation(invocation, semanticModel, cancellationToken):
                receiver = memberAccess.Expression;
                return true;
            case MemberAccessExpressionSyntax memberAccess:
                receiver = memberAccess.Expression;
                return true;
            case ElementAccessExpressionSyntax elementAccess:
                receiver = elementAccess.Expression;
                return true;
            default:
                receiver = null!;
                return false;
        }
    }

    private static bool IsReducedExtensionMethodInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation
            { TargetMethod.ReducedFrom: not null };

    private static SymbolicOperationTransitionResult Exact(
        SymbolicState state,
        SyntaxNode source,
        string detail) =>
        SymbolicOperationTransitionResult.Exact(
            state,
            ImmutableArray.Create(new SymbolicLoweringProvenance(
                "source-completion",
                source.Span,
                detail)));

    private static SymbolicOperationTransitionResult Unsupported(
        SymbolicState state,
        SyntaxNode source,
        string detail) =>
        SymbolicOperationTransitionResult.Unsupported(
            state,
            SymbolicUnknownReason.UnsupportedIrEncoding,
            ImmutableArray.Create(new SymbolicLoweringProvenance(
                "source-completion",
                source.Span,
                detail)));
}
